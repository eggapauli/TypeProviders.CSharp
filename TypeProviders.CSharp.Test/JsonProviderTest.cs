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
using System;

namespace TypeProviders.CSharp.Test
{
    public class JsonProviderTest
    {
        [Fact]
        public void ShouldNotHaveRefactoringWhenAttributeNotSet()
        {
            var code = "class TestProvider { }";
            var provider = CreateCodeRefactoringProvider();
            var action = GetRefactoring(code, provider);
            action.Should().BeNull();
        }

        [Fact]
        public void ShouldNotHaveRefactoringWhenSampleDataArgumentIsMissing()
        {
            var code = @"
[TypeProviders.CSharp.Providers.JsonProvider]
class TestProvider
{
}
";
            var provider = CreateCodeRefactoringProvider();
            var action = GetRefactoring(code, provider);
            action.Should().BeNull();
        }

        [Fact]
        public void ShouldNotHaveRefactoringWhenSampleDataArgumentHasWrongType()
        {
            var code = @"
[TypeProviders.CSharp.Providers.JsonProvider(5)]
class TestProvider
{
}
";
            var provider = CreateCodeRefactoringProvider();
            var action = GetRefactoring(code, provider);
            action.Should().BeNull();
        }

        [Fact]
        public void ShouldHaveRefactoringForSimpleSampleData()
        {
            var code = @"
[TypeProviders.CSharp.Providers.JsonProvider(""{ \""asd\"": \""qwe\"" }"")]
class TestProvider
{
}
";
            var provider = CreateCodeRefactoringProvider();
            var action = GetRefactoring(code, provider);
            action.Should().NotBeNull();
        }

        [Theory]
        [InlineData(@"\""asdqwe\""", "string")]
        [InlineData("5", "int")]
        [InlineData("5.123", "double")]
        [InlineData("true", "bool")]
        [InlineData(@"\""2009-06-15T13:45:30Z\""", "System.DateTime")]
        [InlineData(@"\""7E22EDE9-6D0F-48C2-A280-B36DC859435D\""", "System.Guid")]
        [InlineData(@"\""05:04:03\""", "System.TimeSpan")]
        [InlineData(@"\""http://example.com/path?query#hash\""", "System.Uri")]
        public void ShouldRefactorAccordingToSimpleSampleData(string jsonValue, string expectedType)
        {
            var json = $@"{{ \""Value\"": {jsonValue} }}";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public {expectedType} Value {{ get; }}

    [Newtonsoft.Json.JsonConstructor]
    private TestProvider({expectedType} value)
    {{
        Value = value;
    }}

{CreationMethods("TestProvider", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public void ShouldWorkWithMultipleObjectProperties()
        {
            var json = @"{ \""A\"": 5, \""B\"": \""test\"" }";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public int A {{ get; }}
    public string B {{ get; }}

    [Newtonsoft.Json.JsonConstructor]
    private TestProvider(int a, string b)
    {{
        A = a;
        B = b;
    }}

{CreationMethods("TestProvider", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public void ShouldRefactorAccordingToSampleDataWithNestedObject()
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
    public TestProviderObj Obj {{ get; }}

    public class TestProviderObj
    {{
        public int Value {{ get; }}

        [Newtonsoft.Json.JsonConstructor]
        private TestProviderObj(int value)
        {{
            Value = value;
        }}
    }}

    [Newtonsoft.Json.JsonConstructor]
    private TestProvider(TestProviderObj obj)
    {{
        Obj = obj;
    }}

{CreationMethods("TestProvider", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public void ShouldRefactorAccordingToSampleDataWithSimpleArray()
        {
            var json = @"{ \""Values\"": [ 1, 2, 3 ] }";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public System.Collections.Generic.IReadOnlyList<int> Values {{ get; }}

    [Newtonsoft.Json.JsonConstructor]
    private TestProvider(System.Collections.Generic.IReadOnlyList<int> values)
    {{
        Values = values;
    }}

{CreationMethods("TestProvider", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public void ShouldRefactorAccordingToSampleDataWithArrayOfObjects()
        {
            var json = @"{ \""Values\"": [ { \""Value\"": 1 }, { \""Value\"": 2 }, { \""Value\"": 3 } ] }";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public System.Collections.Generic.IReadOnlyList<TestProviderValuesItem> Values {{ get; }}

    public class TestProviderValuesItem
    {{
        public int Value {{ get; }}

        [Newtonsoft.Json.JsonConstructor]
        private TestProviderValuesItem(int value)
        {{
            Value = value;
        }}
    }}

    [Newtonsoft.Json.JsonConstructor]
    private TestProvider(System.Collections.Generic.IReadOnlyList<TestProviderValuesItem> values)
    {{
        Values = values;
    }}

{CreationMethods("TestProvider", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public void ShouldRefactorAccordingToSampleDataWithArrayOfSimpleArray()
        {
            var json = @"{ \""Values\"": [ [ 1, 2 ], [ 3, 4 ] ] }";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<int>> Values {{ get; }}

    [Newtonsoft.Json.JsonConstructor]
    private TestProvider(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<int>> values)
    {{
        Values = values;
    }}

{CreationMethods("TestProvider", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public void ShouldRefactorAccordingToSampleDataWithArrayOfArrayOfObject()
        {
            var json = @"{ \""Values\"": [ [ { \""Value\"": 1 }, { \""Value\"": 2 } ], [ { \""Value\"": 3 }, { \""Value\"": 4 } ] ] }";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<TestProviderValuesItem>> Values {{ get; }}

    public class TestProviderValuesItem
    {{
        public int Value {{ get; }}

        [Newtonsoft.Json.JsonConstructor]
        private TestProviderValuesItem(int value)
        {{
            Value = value;
        }}
    }}

    [Newtonsoft.Json.JsonConstructor]
    private TestProvider(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<TestProviderValuesItem>> values)
    {{
        Values = values;
    }}

{CreationMethods("TestProvider", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public void ShouldRefactorAccordingToSimpleArray()
        {
            var json = "[1, 2, 3]";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
{CreationMethods("System.Collections.Generic.IReadOnlyList<int>", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Fact]
        public void ShouldRefactorAccordingToObjectArray()
        {
            var json = @"[{ \""Value\"": 1 }, { \""Value\"": 2 }, { \""Value\"": 3 }]";
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{json}"")]";

            var code = attribute + @"
class TestProvider
{
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    public int Value {{ get; }}

    [Newtonsoft.Json.JsonConstructor]
    private TestProvider(int value)
    {{
        Value = value;
    }}

{CreationMethods("System.Collections.Generic.IReadOnlyList<TestProvider>", 1, json)}
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        [Theory]
        [InlineData("Invalid json data", "An error occured while parsing \"Invalid json data\": Error parsing positive infinity value. Path '', line 0, position 0.")]
        [InlineData("http://example.com/not-existing-url", "Getting sample data from \"http://example.com/not-existing-url\" failed with status code 404 (NotFound).")]
        [InlineData("file:///C:/data.json", "Getting sample data from \"file:///C:/data.json\" is not supported. Only \"http\" and \"https\" schemes are allowed.")]
        public void ShouldShowMessageWhenSampleDataIsInvalid(string sampleData, string errorMessage)
        {
            var attribute = $@"[TypeProviders.CSharp.Providers.JsonProvider(""{sampleData}"")]";

            var code = attribute + @"
class TestProvider
{
    public int A { get; }
}
";

            var expectedCode = attribute + $@"
class TestProvider
{{
    /* {errorMessage} */
}}
";
            var provider = CreateCodeRefactoringProvider();
            var document = GetAndApplyRefactoring(code, provider);
            var text = document.GetTextAsync().Result;
            text.ToString().Should().Be(expectedCode);
        }

        static CodeRefactoringProvider CreateCodeRefactoringProvider()
        {
            return new JsonProviderCodeRefactoringProvider();
        }

        string CreationMethods(string typeName, int indentationLevel, string sampleData)
        {
            var lines = new[]
            {
                $"public static async System.Threading.Tasks.Task<{typeName}> LoadAsync(System.Uri uri)",
                "{",
                "    using (var client = new System.Net.Http.HttpClient())",
                "    {",
                "        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);",
                "        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(\"application/json\"));",
                "        request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(\"TypeProviders.CSharp\", \"0.0.1\"));",
                "        var response = await client.SendAsync(request).ConfigureAwait(false);",
                "        response.EnsureSuccessStatusCode();",
                "        var data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);",
                "        return FromData(data);",
                "    }",
                "}",
                "",
                $"public static {typeName} GetSample()",
                "{",
                $"    var data = \"{sampleData.Replace(" ", "")}\";",
                "    return FromData(data);",
                "}",
                "",
                $"public static {typeName} FromData(string data)",
                "{",
                $"    return Newtonsoft.Json.JsonConvert.DeserializeObject<{typeName}>(data);",
                "}"
            };
            return IndentLines(lines, indentationLevel);
        }

        string IndentLines(string[] lines, int indentationLevel)
        {
            const int spacesPerIndentLevel = 4;
            var indentSpaces = indentationLevel * spacesPerIndentLevel;
            var indentedLines = lines
                .Select(x => x != string.Empty
                    ? new string(' ', indentSpaces) + x
                    : x);
            return string.Join(Environment.NewLine, indentedLines);
        }

        static Document GetAndApplyRefactoring(string code, CodeRefactoringProvider codeRefactoringProvider)
        {
            var document = CreateDocument(code);
            var action = GetRefactoring(document, codeRefactoringProvider);
            action.Should().NotBeNull("Can't apply <null> refactoring");
            var operations = action.GetOperationsAsync(CancellationToken.None).Result;
            var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
            var newDocument = solution.GetDocument(document.Id);
            var syntaxRoot = newDocument.GetSyntaxRootAsync().Result;
            var compilation = newDocument.Project.GetCompilationAsync().Result;
            compilation.GetDiagnostics().Should().BeEmpty();
            return newDocument;
        }

        static CodeAction GetRefactoring(string code, CodeRefactoringProvider codeRefactoringProvider)
        {
            var document = CreateDocument(code);
            return GetRefactoring(document, codeRefactoringProvider);
        }

        static CodeAction GetRefactoring(Document document, CodeRefactoringProvider codeRefactoringProvider)
        {
            var root = document.GetSyntaxRootAsync().Result;
            var refactorings = new List<CodeAction>();
            var context = new CodeRefactoringContext(document, root.Span, refactorings.Add, CancellationToken.None);
            codeRefactoringProvider.ComputeRefactoringsAsync(context).Wait();
            return refactorings.SingleOrDefault();
        }

        static Document CreateDocument(string code)
        {
            const string fileNamePrefix = "Test";
            const string fileExt = "cs";
            const string projectName = "TestProject";

            var CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var SystemReference = MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location);
            var SystemCoreReference = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
            var CSharpSymbolsReference = MetadataReference.CreateFromFile(typeof(CSharpCompilation).Assembly.Location);
            var CodeAnalysisReference = MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location);
            var NetHttpReference = MetadataReference.CreateFromFile(typeof(System.Net.Http.HttpClient).Assembly.Location);
            var TypeProvidersAssembly = MetadataReference.CreateFromFile(typeof(JsonProviderAttribute).Assembly.Location);
            var SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);
            var JsonNetReference = MetadataReference.CreateFromFile(typeof(Newtonsoft.Json.JsonConvert).Assembly.Location);

            var projectId = ProjectId.CreateNewId(debugName: projectName);
            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

            var newFileName = $"{fileNamePrefix}.{fileExt}";
            var documentId = DocumentId.CreateNewId(projectId, debugName: newFileName);

            var solution = new AdhocWorkspace()
                .CurrentSolution
                .AddProject(projectId, projectName, projectName, LanguageNames.CSharp)
                .WithProjectCompilationOptions(projectId, compilationOptions)
                .AddMetadataReference(projectId, CorlibReference)
                .AddMetadataReference(projectId, SystemReference)
                .AddMetadataReference(projectId, SystemCoreReference)
                .AddMetadataReference(projectId, CSharpSymbolsReference)
                .AddMetadataReference(projectId, CodeAnalysisReference)
                .AddMetadataReference(projectId, NetHttpReference)
                .AddMetadataReference(projectId, TypeProvidersAssembly)
                .AddMetadataReference(projectId, SystemRuntimeReference)
                .AddMetadataReference(projectId, JsonNetReference)
                .AddDocument(documentId, newFileName, code);

            return solution.GetDocument(documentId);
        }
    }
}
