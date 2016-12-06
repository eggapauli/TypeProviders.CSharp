using System;

namespace TypeProviders.CSharp
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class XmlProviderAttribute : Attribute
    {
        public XmlProviderAttribute(string data)
        {
        }
    }
}
