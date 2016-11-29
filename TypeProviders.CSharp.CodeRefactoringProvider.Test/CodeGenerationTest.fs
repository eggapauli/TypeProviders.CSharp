module TypeProviders.CSharp.Test.CodeGeneration

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CodeActions
open Microsoft.CodeAnalysis.CodeRefactorings
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.Text
open System.Collections.Generic
open System.Linq
open System.Reflection
open System.Threading
open System.Threading.Tasks
open TypeProviders.CSharp
open Xunit
open System
open Swensen.Unquote
open System.Collections.Immutable

let createCodeRefactoringProvider() =
    JsonProviderCodeRefactoringProvider()

let indentLines indentationLevel lines =
    let spacesPerIndentLevel = 4
    let indentSpaces = indentationLevel * spacesPerIndentLevel
    lines
    |> List.map (fun l ->
        if l = ""
        then l
        else (String.replicate indentSpaces " ") + l
    )
    |> String.concat Environment.NewLine

let creationMethods typeName indentationLevel sampleData =
    [
        sprintf "public static async System.Threading.Tasks.Task<%s> LoadAsync(System.Uri uri)" typeName
        "{"
        "    using (var client = new System.Net.Http.HttpClient())"
        "    {"
        "        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);"
        "        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(\"application/json\"));"
        "        request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(\"TypeProviders.CSharp\", \"0.0.1\"));"
        "        var response = await client.SendAsync(request).ConfigureAwait(false);"
        "        response.EnsureSuccessStatusCode();"
        "        var data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);"
        "        return FromData(data);"
        "    }"
        "}"
        ""
        sprintf "public static %s GetSample()" typeName
        "{"
        sprintf "    var data = \"%s\";" sampleData
        "    return FromData(data);"
        "}"
        ""
        sprintf "public static %s FromData(string data)" typeName
        "{"
        sprintf "    return Newtonsoft.Json.JsonConvert.DeserializeObject<%s>(data);" typeName
        "}"
    ]
    |> indentLines indentationLevel

let metaDataReferenceFromType<'a> =
    typeof<'a>.Assembly.Location
    |> MetadataReference.CreateFromFile

let createDocument (code: string) =
    let fileNamePrefix = "Test"
    let fileExt = "cs"
    let projectName = "TestProject"

    let corlibReference = metaDataReferenceFromType<obj>
    let systemReference = metaDataReferenceFromType<Uri>
    let systemCoreReference = metaDataReferenceFromType<Enumerable>
    let cSharpSymbolsReference = metaDataReferenceFromType<CSharpCompilation>
    let codeAnalysisReference = metaDataReferenceFromType<Compilation>
    let netHttpReference = MetadataReference.CreateFromFile(Assembly.Load("System.Net.Http, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location)
    let typeProvidersAssembly = metaDataReferenceFromType<TypeProviders.CSharp.JsonProviderAttribute>
    let systemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location)
    let jsonNetReference = metaDataReferenceFromType<Newtonsoft.Json.JsonConvert>

    let projectId = ProjectId.CreateNewId(debugName = projectName)
    let compilationOptions = CSharpCompilationOptions OutputKind.DynamicallyLinkedLibrary

    let newFileName = sprintf "%s.%s" fileNamePrefix fileExt
    let documentId = DocumentId.CreateNewId(projectId, debugName = newFileName)

    let workspace = new AdhocWorkspace()
    let solution =
        workspace
            .CurrentSolution
            .AddProject(projectId, projectName, projectName, LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, compilationOptions)
            .AddMetadataReference(projectId, corlibReference)
            .AddMetadataReference(projectId, systemReference)
            .AddMetadataReference(projectId, systemCoreReference)
            .AddMetadataReference(projectId, cSharpSymbolsReference)
            .AddMetadataReference(projectId, codeAnalysisReference)
            .AddMetadataReference(projectId, netHttpReference)
            .AddMetadataReference(projectId, typeProvidersAssembly)
            .AddMetadataReference(projectId, systemRuntimeReference)
            .AddMetadataReference(projectId, jsonNetReference)
            .AddDocument(documentId, newFileName, code)

    solution.GetDocument documentId

let getRefactoring (document: Document) (codeRefactoringProvider: CodeRefactoringProvider) =
    let root = document.GetSyntaxRootAsync().Result
    let refactorings = System.Collections.Generic.List<_>()
    let context = new CodeRefactoringContext(document, root.Span, Action<_>(refactorings.Add), CancellationToken.None);
    codeRefactoringProvider.ComputeRefactoringsAsync(context).Wait()
    refactorings
    |> Seq.tryHead

let createDocumentAndGetRefactoring code codeRefactoringProvider =
    let document = createDocument code
    getRefactoring document codeRefactoringProvider

let getAndApplyRefactoring code codeRefactoringProvider =
    let document = createDocument code
    match getRefactoring document codeRefactoringProvider with
    | Some action ->
        let operations = action.GetOperationsAsync(CancellationToken.None).Result
        let solution =
            operations
            |> Seq.ofType<ApplyChangesOperation>
            |> Seq.exactlyOne
            |> fun o -> o.ChangedSolution
        let newDocument = solution.GetDocument(document.Id)
        let syntaxRoot = newDocument.GetSyntaxRootAsync().Result
        let compilation = newDocument.Project.GetCompilationAsync().Result
        compilation.GetDiagnostics() =! ImmutableArray<Diagnostic>.Empty
        newDocument
    | None -> failwith "Can't apply <null> refactoring"

[<Fact>]
let ``Should not have refactoring when attribute not set``() =
    let code = "class TestProvider { }"
    let provider = createCodeRefactoringProvider()
    let action = createDocumentAndGetRefactoring code provider
    action.IsSome =! false

[<Fact>]
let ``Should not have refactoring when sample data argument is missing``() =
    let code = """
[TypeProviders.CSharp.JsonProvider]
class TestProvider
{
}
"""
    let provider = createCodeRefactoringProvider()
    let action = createDocumentAndGetRefactoring code provider
    action.IsSome =! false

[<Fact>]
let ``Should not have refactoring when sample data argument has wrong type``() =
    let code = """
[TypeProviders.CSharp.JsonProvider(5)]
class TestProvider
{
}
"""
    let provider = createCodeRefactoringProvider()
    let action = createDocumentAndGetRefactoring code provider
    action.IsSome =! false

[<Fact>]
let ``Should have refactoring for simple sample data``() =
    let code = """
[TypeProviders.CSharp.JsonProvider("{ \"asd\": \"qwe\" }")]
class TestProvider
{
}
"""
    let provider = createCodeRefactoringProvider()
    let action = createDocumentAndGetRefactoring code provider
    action.IsSome =! true

[<Fact>]
let ``Should refactor according to complex object``() =
    let json = """{
    \"IntValue\": 1,
    \"DateTimeValue\": \"2009-06-15T13:45:30Z\",
    \"SimpleArray\": [ 1, 2, 3 ],
    \"SimpleArrayOfArray\": [ [ 1, 2 ], [ 3, 4 ] ],
    \"NestedObject\": {
        \"A\": {
            \"B\": \"C\"
        }
    },
    \"CollidingNames\": [
        { \"CollidingName\": \"asd\" },
        { \"CollidingName\": \"qwe\" }
    ]
}"""
    let minifiedJson = System.Text.RegularExpressions.Regex.Replace(json, "\r\n\s*", "")
    let attribute = sprintf """[TypeProviders.CSharp.JsonProvider("%s")]""" minifiedJson

    let code = attribute + """
class TestProvider
{
}
"""

    let expectedCode = attribute + (sprintf """
class TestProvider
{
    public class Root
    {
        public int IntValue { get; }
        public System.DateTime DateTimeValue { get; }
        public System.Collections.Generic.IReadOnlyList<int> SimpleArray { get; }
        public System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<int>> SimpleArrayOfArray { get; }
        public NestedObject NestedObject { get; }
        public System.Collections.Generic.IReadOnlyList<CollidingName_> CollidingNames { get; }

        private Root(int intValue, System.DateTime dateTimeValue, System.Collections.Generic.IReadOnlyList<int> simpleArray, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<int>> simpleArrayOfArray, NestedObject nestedObject, System.Collections.Generic.IReadOnlyList<CollidingName_> collidingNames)
        {
            IntValue = intValue;
            DateTimeValue = dateTimeValue;
            SimpleArray = simpleArray;
            SimpleArrayOfArray = simpleArrayOfArray;
            NestedObject = nestedObject;
            CollidingNames = collidingNames;
        }
    }

    public class NestedObject
    {
        public A A { get; }

        private NestedObject(A a)
        {
            A = a;
        }
    }

    public class A
    {
        public string B { get; }

        private A(string b)
        {
            B = b;
        }
    }

    public class CollidingName_
    {
        public string CollidingName { get; }

        private CollidingName_(string collidingName)
        {
            CollidingName = collidingName;
        }
    }

%s
}
""" <| creationMethods "Root" 1 minifiedJson)
    let provider = createCodeRefactoringProvider()
    let document = getAndApplyRefactoring code provider
    let text = document.GetTextAsync().Result.ToString()
    text =! expectedCode

module String =
    let replace (oldValue: string) newValue (text: string) =
        text.Replace(oldValue, newValue)

[<Theory>]
[<InlineData("Invalid json data")>]
[<InlineData("http://example.com")>]
[<InlineData("http://example.com/not-existing-url")>]
[<InlineData("file:///C:/data.json")>]
let ``Should show message when sample data is invalid``sampleData =
    let attribute = sprintf """[TypeProviders.CSharp.JsonProvider("%s")]""" sampleData

    let code = attribute + """
class TestProvider
{
    public int A { get; }
}
"""

    let expectedPattern =
        (System.Text.RegularExpressions.Regex.Escape attribute) + """
class TestProvider
\{
    /\* .* \*/
\}
"""
    let provider = createCodeRefactoringProvider()
    let document = getAndApplyRefactoring code provider
    let text = document.GetTextAsync().Result.ToString()

    test <@ System.Text.RegularExpressions.Regex
        .IsMatch(text, expectedPattern, System.Text.RegularExpressions.RegexOptions.Singleline) @>
