module TypeProviders.CSharp.Test.CodeGeneration.Json

open TypeProviders.CSharp.CodeRefactoringProvider
open Xunit
open Swensen.Unquote

let private createCodeRefactoringProvider() =
    JsonProviderCodeRefactoringProvider()

let private creationMethods typeName sampleData =
    [
        sprintf "public static %s Parse(System.IO.Stream dataStream)" typeName
        "{"
        "    using (var textReader = new System.IO.StreamReader(dataStream))"
        "    using (var jsonTextReader = new Newtonsoft.Json.JsonTextReader(textReader))"
        "    {"
        "        var serializer = new Newtonsoft.Json.JsonSerializer();"
        sprintf "        return serializer.Deserialize<%s>(jsonTextReader);" typeName
        "    }"
        "}"
        ""
        sprintf "public static async System.Threading.Tasks.Task<%s> LoadAsync(System.Uri uri)" typeName
        "{"
        "    using (var client = new System.Net.Http.HttpClient())"
        "    {"
        "        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);"
        "        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(\"application/json\"));"
        "        request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(\"TypeProviders.CSharp\", \"0.0.1\"));"
        "        var response = await client.SendAsync(request).ConfigureAwait(false);"
        "        response.EnsureSuccessStatusCode();"
        "        var dataStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);"
        "        return Parse(dataStream);"
        "    }"
        "}"
        ""
        sprintf "public static %s Load(string filePath)" typeName
        "{"
        "    using (var dataStream = System.IO.File.OpenRead(filePath))"
        "        return Parse(dataStream);"
        "}"
        ""
        sprintf "public static %s Parse(string data)" typeName
        "{"
        "    var dataBytes = System.Text.Encoding.Default.GetBytes(data);"
        "    using (var dataStream = new System.IO.MemoryStream(dataBytes))"
        "        return Parse(dataStream);"
        "}"
        ""
        sprintf "public static %s GetSample()" typeName
        "{"
        sprintf "    var sampleData = \"%s\";" sampleData
        "    return Parse(sampleData);"
        "}"
    ]
    |> TestSetup.indentLines 1

let private createDocument code =
    let jsonNetReference = TestSetup.metaDataReferenceFromType<Newtonsoft.Json.JsonConvert>

    TestSetup.createDocument [ jsonNetReference ] code

let private getAndApplyRefactoring =
    TestSetup.getAndApplyRefactoring (createCodeRefactoringProvider())

[<Fact>]
let ``Should refactor according to complex object``() =
    let data = """{
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
    ],
    \"Private\": true
}"""
    let minifiedData = System.Text.RegularExpressions.Regex.Replace(data, "\r\n\s*", "")
    let attribute = sprintf """[TypeProviders.CSharp.JsonProvider("%s")]""" minifiedData

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
        public bool Private { get; }

        public Root(int intValue, System.DateTime dateTimeValue, System.Collections.Generic.IReadOnlyList<int> simpleArray, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<int>> simpleArrayOfArray, NestedObject nestedObject, System.Collections.Generic.IReadOnlyList<CollidingName_> collidingNames, bool @private)
        {
            IntValue = intValue;
            DateTimeValue = dateTimeValue;
            SimpleArray = simpleArray;
            SimpleArrayOfArray = simpleArrayOfArray;
            NestedObject = nestedObject;
            CollidingNames = collidingNames;
            Private = @private;
        }
    }

    public class NestedObject
    {
        public A A { get; }

        public NestedObject(A a)
        {
            A = a;
        }
    }

    public class A
    {
        public string B { get; }

        public A(string b)
        {
            B = b;
        }
    }

    public class CollidingName_
    {
        public string CollidingName { get; }

        public CollidingName_(string collidingName)
        {
            CollidingName = collidingName;
        }
    }

%s
}
""" <| creationMethods "Root" minifiedData)
    let document = createDocument code |> getAndApplyRefactoring
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
    let document = createDocument code |> getAndApplyRefactoring
    let text = document.GetTextAsync().Result.ToString()

    test <@ System.Text.RegularExpressions.Regex
        .IsMatch(text, expectedPattern, System.Text.RegularExpressions.RegexOptions.Singleline) @>
