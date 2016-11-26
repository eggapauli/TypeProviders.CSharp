using CodeGeneration.Roslyn;
using System;
using System.Diagnostics;

namespace TypeProviders.CSharp.BuildTimeGeneration.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    [CodeGenerationAttribute("TypeProviders.CSharp.BuildTimeGeneration.JsonProviderGenerator, TypeProviders.CSharp.BuildTimeGeneration, Version=" + Globals.AssemblyVersion + ", Culture=neutral, PublicKeyToken=null")]
    [Conditional("CodeGeneration")]
    public class JsonProviderAttribute : Attribute
    {
        public JsonProviderAttribute(string data)
        {
        }
    }
}
