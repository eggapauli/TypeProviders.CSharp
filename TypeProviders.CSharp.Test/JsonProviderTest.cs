using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TypeProviders.CSharp.Providers;
using Xunit;

namespace TypeProviders.CSharp.Test
{
    public class JsonProviderTest
    {
        [Fact]
        public async Task ShouldNotHaveRefactoringWhenAttributeNotSet()
        {
            var code = "class TestProvider { }";
            var provider = CreateCodeRefactoringProviderForDataStructure();
            var action = await GetRefactoring(code, provider);
            action.Should().BeNull();
        }

        [Fact]
        public async Task ShouldNotHaveRefactoringWhenSampleDataArgumentIsMissing()
        {
            var code = @"
[TypeProviders.CSharp.Providers.JsonProvider]
class TestProvider
{
}
";
            var provider = CreateCodeRefactoringProviderForDataStructure();
            var action = await GetRefactoring(code, provider);
            action.Should().BeNull();
        }

        [Fact]
        public async Task ShouldNotHaveRefactoringWhenSampleDataArgumentHasWrongType()
        {
            var code = @"
[TypeProviders.CSharp.Providers.JsonProvider(5)]
class TestProvider
{
}
";
            var provider = CreateCodeRefactoringProviderForDataStructure();
            var action = await GetRefactoring(code, provider);
            action.Should().BeNull();
        }

        [Fact]
        public async Task ShouldHaveRefactoringForSimpleSampleData()
        {
            var code = @"
[TypeProviders.CSharp.Providers.JsonProvider(""{ \""asd\"": \""qwe\"" }"")]
class TestProvider
{
}
";
            var provider = CreateCodeRefactoringProviderForDataStructure();
            var action = await GetRefactoring(code, provider);
            action.Should().NotBeNull();
        }

        [Theory]
        [InlineData(@"\""asdqwe\""", "string")]
        [InlineData("5", "int")]
        [InlineData("5.123", "double")]
        [InlineData("true", "bool")]
        [InlineData(@"\""2009-06-15T13:45:30.0000000Z\""", "System.DateTime")]
        [InlineData(@"\""7E22EDE9-6D0F-48C2-A280-B36DC859435D\""", "System.Guid")]
        [InlineData(@"\""05:04:03\""", "System.TimeSpan")]
        [InlineData(@"\""http://example.com/path?query#hash\""", "System.Uri")]
        public async Task ShouldRefactorAccordingToSimpleSampleData(string jsonValue, string expectedType)
        {
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{{ \""Value\"": {jsonValue} }}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public {expectedType} Value {{ get; private set; }}
}}
";
            var provider = CreateCodeRefactoringProviderForDataStructure();
            var document = await GetAndApplyRefactoring(code, provider);
            var text = await document.GetTextAsync();
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public async Task ShouldRefactorAccordingToSampleDataWithNestedObject()
        {
            var json = @"{ \""Obj\"": { \""Value\"": 5 } }";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public TestProviderObj Obj {{ get; private set; }}

    public class TestProviderObj
    {{
        public int Value {{ get; private set; }}
    }}
}}
";
            var provider = CreateCodeRefactoringProviderForDataStructure();
            var document = await GetAndApplyRefactoring(code, provider);
            var text = await document.GetTextAsync();
            text.ToString().Should().Be(expectedCode);
        }

        private static JsonProviderCodeRefactoringProvider CreateCodeRefactoringProviderForDataStructure()
        {
            return new JsonProviderCodeRefactoringProvider { AddCreationMethods = false };
        }

        static async Task<Document> GetAndApplyRefactoring(string code, CodeRefactoringProvider codeRefactoringProvider)
        {
            var document = CreateDocument(code);
            var action = await GetRefactoring(document, codeRefactoringProvider);
            action.Should().NotBeNull("Can't apply <null> refactoring");
            var operations = await action.GetOperationsAsync(CancellationToken.None);
            var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
            return solution.GetDocument(document.Id);
        }

        static async Task<CodeAction> GetRefactoring(string code, CodeRefactoringProvider codeRefactoringProvider)
        {
            var document = CreateDocument(code);
            return await GetRefactoring(document, codeRefactoringProvider);
        }

        static async Task<CodeAction> GetRefactoring(Document document, CodeRefactoringProvider codeRefactoringProvider)
        {
            var root = await document.GetSyntaxRootAsync();
            var refactorings = new List<CodeAction>();
            var context = new CodeRefactoringContext(document, root.Span, refactorings.Add, CancellationToken.None);
            await codeRefactoringProvider.ComputeRefactoringsAsync(context);
            return refactorings.SingleOrDefault();
        }

        static Document CreateDocument(string code)
        {
            const string fileNamePrefix = "Test";
            const string fileExt = "cs";
            const string projectName = "TestProject";

            var CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var SystemCoreReference = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
            var CSharpSymbolsReference = MetadataReference.CreateFromFile(typeof(CSharpCompilation).Assembly.Location);
            var CodeAnalysisReference = MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location);
            var TypeProvidersAssembly = MetadataReference.CreateFromFile(typeof(JsonProviderAttribute).Assembly.Location);

            var projectId = ProjectId.CreateNewId(debugName: projectName);

            var solution = new AdhocWorkspace()
                .CurrentSolution
                .AddProject(projectId, projectName, projectName, LanguageNames.CSharp)
                .AddMetadataReference(projectId, CorlibReference)
                .AddMetadataReference(projectId, SystemCoreReference)
                .AddMetadataReference(projectId, CSharpSymbolsReference)
                .AddMetadataReference(projectId, CodeAnalysisReference)
                .AddMetadataReference(projectId, TypeProvidersAssembly)
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location));

            var newFileName = $"{fileNamePrefix}.{fileExt}";
            var documentId = DocumentId.CreateNewId(projectId, debugName: newFileName);
            solution = solution.AddDocument(documentId, newFileName, SourceText.From(code));
            return solution.GetDocument(documentId);
        }
    }
}
