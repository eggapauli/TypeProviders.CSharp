using System;

namespace TypeProviders.CSharp.Providers
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class JsonProviderAttribute : Attribute
    {
        public JsonProviderAttribute(string sampleJson)
        {
        }
    }
}
