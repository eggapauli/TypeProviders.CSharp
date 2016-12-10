using TypeProviders.CSharp.BuildTimeGeneration.Attributes;

namespace TypeProviders.CSharp.BuildTimeGeneration.Test
{
    [JsonProvider("{\"a\": 1, \"b\": \"text\"}")]
    public partial class TestProvider
    {
    }
}
