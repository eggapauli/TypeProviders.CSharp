using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
            var action = await GetRefactoring(code);
            action.Should().BeNull();
        }

        static async Task<CodeAction> GetRefactoring(string code)
        {
            var document = CreateDocument(code);
            return await GetRefactoring(document);
        }

        static async Task<CodeAction> GetRefactoring(Document document)
        {
            var root = await document.GetSyntaxRootAsync();
            var refactorings = new List<CodeAction>();
            var context = new CodeRefactoringContext(document, root.Span, refactorings.Add, CancellationToken.None);
            var provider = new JsonProviderCodeRefactoringProvider();
            await provider.ComputeRefactoringsAsync(context);
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
