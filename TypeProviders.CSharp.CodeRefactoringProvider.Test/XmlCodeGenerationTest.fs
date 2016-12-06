module TypeProviders.CSharp.Test.CodeGeneration.Xml

open TypeProviders.CSharp.CodeRefactoringProvider
open Xunit
open Swensen.Unquote

let private createCodeRefactoringProvider() =
    XmlProviderCodeRefactoringProvider()

let private creationMethods typeName sampleData =
    [
        sprintf "public static %s Parse(System.IO.Stream dataStream)" typeName
        "{"
        sprintf "    return new %s(System.Xml.Linq.XElement.Load(dataStream));" typeName
        "}"
        ""
        sprintf "public static async System.Threading.Tasks.Task<%s> LoadAsync(System.Uri uri)" typeName
        "{"
        "    using (var client = new System.Net.Http.HttpClient())"
        "    {"
        "        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);"
        "        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(\"application/xml\"));"
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
        ""
        "private static string GetSimpleValue(System.Xml.Linq.XElement e, string propertyName)"
        "{"
        "    var childElement = System.Linq.Enumerable.FirstOrDefault(e.Elements(), p => p.Name.LocalName.Equals(propertyName, System.StringComparison.InvariantCultureIgnoreCase));"
        "    if (childElement != null)"
        "        return childElement.Value;"
        "    var childAttribute = System.Linq.Enumerable.FirstOrDefault(e.Attributes(), p => p.Name.LocalName.Equals(propertyName, System.StringComparison.InvariantCultureIgnoreCase));"
        "    if (childAttribute != null)"
        "        return childAttribute.Value;"
        """    throw new System.ArgumentException($"Unable to parse XML data. Element {e} doesn't have an attribute or a child element named \"{propertyName}\"");"""
        "}"
        ""
        "private static System.Xml.Linq.XElement GetChildElement(System.Xml.Linq.XElement e, string propertyName)"
        "{"
        "    var childElement = System.Linq.Enumerable.FirstOrDefault(e.Elements(), p => p.Name.LocalName.Equals(propertyName, System.StringComparison.InvariantCultureIgnoreCase));"
        "    if (childElement == null)"
        """        throw new System.ArgumentException($"Unable to parse XML data. Element {e} doesn't have a child element named \"{propertyName}\"");"""
        "    return childElement;"
        "}"
    ]
    |> TestSetup.indentLines 1

let private createDocument code =
    let systemXml = TestSetup.metaDataReferenceFromType<System.Xml.XmlReader>
    let systemXmlLinq = TestSetup.metaDataReferenceFromType<System.Xml.Linq.XElement>

    TestSetup.createDocument [ systemXml; systemXmlLinq ] code

let private getAndApplyRefactoring =
    TestSetup.getAndApplyRefactoring (createCodeRefactoringProvider())

[<Fact>]
let ``Should refactor according to complex object``() =
    let data = """<Root>
    <IntValue>1</IntValue>
    <SimpleArray>1</SimpleArray>
    <SimpleArray>2</SimpleArray>
    <SimpleArray>3</SimpleArray>
    <NestedObject>
        <A>
            <B C=\"D\" />
        </A>
    </NestedObject>
    <CollidingNames>
        <CollidingName CollidingName=\"asd\" />
        <CollidingName CollidingName=\"qwe\" />
    </CollidingNames>
    <Private>true</Private>
    <HetereogeneousArray A=\"1\"></HetereogeneousArray>
    <HetereogeneousArray B=\"Test\"></HetereogeneousArray>
    <HetereogeneousArray C=\"1.234\"></HetereogeneousArray>
</Root>"""
    let minifiedData = System.Text.RegularExpressions.Regex.Replace(data, "\r\n\s*", "")
    let attribute = sprintf """[TypeProviders.CSharp.XmlProvider("%s")]""" minifiedData

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
        public System.Collections.Generic.IReadOnlyList<int> SimpleArray { get; }
        public NestedObject NestedObject { get; }
        public System.Collections.Generic.IReadOnlyList<CollidingName_> CollidingNames { get; }
        public bool Private { get; }
        public System.Collections.Generic.IReadOnlyList<HetereogeneousArray> HetereogeneousArray { get; }

        public Root(int intValue, System.Collections.Generic.IReadOnlyList<int> simpleArray, NestedObject nestedObject, System.Collections.Generic.IReadOnlyList<CollidingName_> collidingNames, bool @private, System.Collections.Generic.IReadOnlyList<HetereogeneousArray> hetereogeneousArray)
        {
            IntValue = intValue;
            SimpleArray = simpleArray;
            NestedObject = nestedObject;
            CollidingNames = collidingNames;
            Private = @private;
            HetereogeneousArray = hetereogeneousArray;
        }

        public Root(System.Xml.Linq.XElement e)
        {
            IntValue = int.Parse(GetSimpleValue(e, "IntValue"));
            SimpleArray = new System.Collections.Generic.List<int>(System.Linq.Enumerable.Select(e.Elements(), p => int.Parse(p.Value)));
            NestedObject = new NestedObject(GetChildElement(e, "NestedObject"));
            CollidingNames = new System.Collections.Generic.List<CollidingName_>(System.Linq.Enumerable.Select(e.Elements(), p => new CollidingName_(p)));
            Private = bool.Parse(GetSimpleValue(e, "Private"));
            HetereogeneousArray = new System.Collections.Generic.List<HetereogeneousArray>(System.Linq.Enumerable.Select(e.Elements(), p => new HetereogeneousArray(p)));
        }
    }

    public class NestedObject
    {
        public A A { get; }

        public NestedObject(A a)
        {
            A = a;
        }

        public NestedObject(System.Xml.Linq.XElement e)
        {
            A = new A(GetChildElement(e, "A"));
        }
    }

    public class A
    {
        public B B { get; }

        public A(B b)
        {
            B = b;
        }

        public A(System.Xml.Linq.XElement e)
        {
            B = new B(GetChildElement(e, "B"));
        }
    }

    public class B
    {
        public string C { get; }

        public B(string c)
        {
            C = c;
        }

        public B(System.Xml.Linq.XElement e)
        {
            C = GetSimpleValue(e, "C");
        }
    }

    public class CollidingName_
    {
        public string CollidingName { get; }

        public CollidingName_(string collidingName)
        {
            CollidingName = collidingName;
        }

        public CollidingName_(System.Xml.Linq.XElement e)
        {
            CollidingName = GetSimpleValue(e, "CollidingName");
        }
    }

    public class HetereogeneousArray
    {
        public int? A { get; }
        public string B { get; }
        public decimal? C { get; }

        public HetereogeneousArray(int? a, string b, decimal? c)
        {
            A = a;
            B = b;
            C = c;
        }

        public HetereogeneousArray(System.Xml.Linq.XElement e)
        {
            A = int.Parse(e.Value);
            B = GetSimpleValue(e, "B");
            C = decimal.Parse(e.Value);
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
[<InlineData("Invalid xml data")>]
[<InlineData("http://example.com")>]
[<InlineData("http://example.com/not-existing-url")>]
[<InlineData("file:///C:/data.xml")>]
let ``Should show message when sample data is invalid``sampleData =
    let attribute = sprintf """[TypeProviders.CSharp.XmlProvider("%s")]""" sampleData

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
