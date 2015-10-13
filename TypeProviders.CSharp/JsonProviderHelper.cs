using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace TypeProviders.CSharp
{
    public static class TypeProviderHelper
    {
        public static Optional<string> TryGetTypeProviderSampleData(TypeDeclarationSyntax typeDecl, string typeProviderAttributeName, SemanticModel semanticModel)
        {
            var attributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeProviderAttributeName);
            if (attributeSymbol == null) return new Optional<string>();

            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
            
            var attribute = typeSymbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass.Equals(attributeSymbol));
            if (attribute == null) return new Optional<string>();

            var sampleSourceArgument = attribute.ConstructorArguments.FirstOrDefault();
            if (sampleSourceArgument.IsNull) return new Optional<string>();

            var sampleData = sampleSourceArgument.Value as string;
            if (sampleData == null) return new Optional<string>();

            return sampleData;
        }
    }
}
