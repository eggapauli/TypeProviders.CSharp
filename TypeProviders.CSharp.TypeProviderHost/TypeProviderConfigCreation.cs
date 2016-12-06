using Microsoft.FSharp.Core.CompilerServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TypeProviders.CSharp
{
    public static class TypeProviderHost
    {
        private class ImportedBinaryMock
        {
            public string FileName { get; }

            public ImportedBinaryMock(string fileName)
            {
                FileName = fileName;
            }
        }

        private class TcImportsMock
        {
            private IReadOnlyCollection<ImportedBinaryMock> dllInfos;

            public Microsoft.FSharp.Core.FSharpOption<TcImportsMock> Base { get; }

            public TcImportsMock(IEnumerable<ImportedBinaryMock> dllInfos, Microsoft.FSharp.Core.FSharpOption<TcImportsMock> @base)
            {
                this.dllInfos = dllInfos.ToList();
                Base = @base;
            }
        }

        private class SystemRuntimeContainsTypeMock : Microsoft.FSharp.Core.FSharpFunc<string, bool>
        {
            private TcImportsMock tcImports;

            public SystemRuntimeContainsTypeMock(TcImportsMock tcImports)
            {
                this.tcImports = tcImports;
            }

            public override bool Invoke(string func)
            {
                return Type.GetType(func).Assembly.Equals(typeof(object).Assembly);
            }
        }

        public static TypeProviderConfig CreateConfig(Assembly runtimeAssembly, IEnumerable<Assembly> additionalAssemblies)
        {
            var referencedAssemblies = runtimeAssembly.GetReferencedAssemblies();
            var requiredAssemblies = new[] {
                typeof(object).Assembly,
                Assembly.Load("FSharp.Core"),
                Assembly.Load("FSharp.Data.DesignTime")
            };

            var assemblies = new[] { runtimeAssembly }
                .Concat(requiredAssemblies)
                .Concat(additionalAssemblies)
                .Select(asm => new ImportedBinaryMock(asm.Location));
            var @base = new TcImportsMock(new ImportedBinaryMock[0], Microsoft.FSharp.Core.FSharpOption<TcImportsMock>.None);
            var tcImports = new TcImportsMock(assemblies, Microsoft.FSharp.Core.FSharpOption<TcImportsMock>.Some(@base));
            var systemRuntimeContainsType = new SystemRuntimeContainsTypeMock(tcImports);

            return new TypeProviderConfig(systemRuntimeContainsType)
            {
                RuntimeAssembly = runtimeAssembly.FullName
            };
        }
    }
}
