using System;

namespace TypeProviders.CSharp
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class JsonProviderAttribute : Attribute
    {
        public JsonProviderAttribute(string data)
        {
        }
    }
}
