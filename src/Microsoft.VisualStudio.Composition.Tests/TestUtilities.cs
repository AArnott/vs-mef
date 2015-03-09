﻿namespace Microsoft.VisualStudio.Composition.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Composition.Hosting;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;
    using Xunit.Abstractions;
    using Xunit.Sdk;
    using CompositionFailedException = Microsoft.VisualStudio.Composition.CompositionFailedException;
    using MefV1 = System.ComponentModel.Composition;

    internal static class TestUtilities
    {
        /// <summary>
        /// Gets the timeout value to use for tests that do not expect the timeout to occur.
        /// </summary>
        internal static TimeSpan UnexpectedTimeout
        {
            get
            {
                return Debugger.IsAttached
                    ? Timeout.InfiniteTimeSpan
                    : TimeSpan.FromSeconds(2);
            }
        }

        /// <summary>
        /// Gets a timeout value to use for tests that expect the timeout to occur.
        /// </summary>
        internal static TimeSpan ExpectedTimeout
        {
            get
            {
                return Debugger.IsAttached
                    ? TimeSpan.FromSeconds(5)
                    : TimeSpan.FromMilliseconds(200);
            }
        }

        internal static async Task<ExportProvider> CreateContainerAsync(this CompositionConfiguration configuration, bool runtime, ITestOutputHelper output)
        {
            Requires.NotNull(configuration, "configuration");
            Requires.NotNull(output, nameof(output));

            if (runtime)
            {
                var runtimeComposition = RuntimeComposition.CreateRuntimeComposition(configuration);

                // Round-trip serialization to make sure the result is equivalent.
                var cacheManager = new CachedComposition();
                var ms = new MemoryStream();
                await cacheManager.SaveAsync(runtimeComposition, ms);
                output.WriteLine("Cache file size: {0}", ms.Length);
                ms.Position = 0;
                var deserializedRuntimeComposition = await cacheManager.LoadRuntimeCompositionAsync(ms);
                Assert.Equal(runtimeComposition, deserializedRuntimeComposition);

                return runtimeComposition.CreateExportProviderFactory().CreateExportProvider();
            }
            else
            {
                string basePath = Path.GetTempFileName();
                string assemblyPath = basePath + ".dll";
                var compiledCacheManager = new CompiledComposition
                {
                    AssemblyName = Path.GetFileNameWithoutExtension(assemblyPath),
                    BuildOutput = Console.Out,
                };

                if (Debugger.IsAttached)
                {
                    compiledCacheManager.Optimize = false;
                    using (var pdb = File.Open(basePath + ".pdb", FileMode.Create))
                    {
                        using (var source = File.Open(basePath + ".cs", FileMode.Create))
                        {
                            compiledCacheManager.Optimize = false;
                            compiledCacheManager.PdbSymbols = pdb;
                            compiledCacheManager.Source = source;
                            using (var assemblyStream = File.Open(assemblyPath, FileMode.CreateNew))
                            {
                                await compiledCacheManager.SaveAsync(configuration, assemblyStream);
                            }
                        }
                    }

                    var exportProviderFactory = CompiledComposition.LoadExportProviderFactory(assemblyPath);
                    return exportProviderFactory.CreateExportProvider();
                }
                else
                {
                    Stream sourceFileStream = null;
#if DEBUG
                    sourceFileStream = new MemoryStream();
#endif
                    try
                    {
                        compiledCacheManager.Source = sourceFileStream;
                        var assemblyStream = new MemoryStream();
                        compiledCacheManager.SaveAsync(configuration, assemblyStream).Wait();
                        assemblyStream.Position = 0;
                        var exportProvider = (await compiledCacheManager.LoadExportProviderFactoryAsync(assemblyStream)).CreateExportProvider();
                        return exportProvider;
                    }
                    finally
                    {
                        if (sourceFileStream != null)
                        {
                            bool includeLineNumbers;
                            TextWriter sourceFileWriter;
                            if (sourceFileStream.Length < 200 * 1024) // the test results window doesn't do well with large output
                            {
                                includeLineNumbers = true;
                                sourceFileWriter = Console.Out;
                            }
                            else
                            {
                                // Write to a file instead and then emit its path to the output window.
                                string sourceFileName = Path.GetTempFileName() + ".cs";
                                sourceFileWriter = new StreamWriter(File.OpenWrite(sourceFileName));
                                output.WriteLine("Source file written to: {0}", sourceFileName);
                                includeLineNumbers = false;
                            }

                            sourceFileStream.Position = 0;
                            var sourceFileReader = new StreamReader(sourceFileStream);
                            int lineNumber = 0;
                            string line;
                            while ((line = sourceFileReader.ReadLine()) != null)
                            {
                                if (includeLineNumbers)
                                {
                                    sourceFileWriter.Write("Line {0,5}: ", ++lineNumber);
                                }

                                sourceFileWriter.WriteLine(line);
                            }

                            sourceFileWriter.Flush();
                            if (sourceFileWriter != Console.Out)
                            {
                                sourceFileWriter.Close();
                            }
                        }
                    }
                }
            }
        }

        internal static Task<ExportProvider> CreateContainer(ITestOutputHelper output, params Type[] parts)
        {
            return CreateContainerAsync(output, parts);
        }

        internal static async Task<ExportProvider> CreateContainerAsync(ITestOutputHelper output, params Type[] parts)
        {
            var catalog = await new AttributedPartDiscovery().CreatePartsAsync(parts);
            var configuration = await CompositionConfiguration.Create(catalog)
                .CreateContainerAsync(true, output);
            return configuration;
        }

        internal static IContainer CreateContainerV1(params Type[] parts)
        {
            Requires.NotNull(parts, "parts");
            var catalog = new MefV1.Hosting.TypeCatalog(parts);
            return CreateContainerV1(catalog);
        }

        internal static IContainer CreateContainerV1(IReadOnlyList<Assembly> assemblies, Type[] parts)
        {
            Requires.NotNull(parts, "parts");
            var catalogs = assemblies.Select(a => new MefV1.Hosting.AssemblyCatalog(a))
                .Concat<MefV1.Primitives.ComposablePartCatalog>(new[] { new MefV1.Hosting.TypeCatalog(parts) });
            var catalog = new MefV1.Hosting.AggregateCatalog(catalogs);

            return CreateContainerV1(catalog);
        }

        private static IContainer CreateContainerV1(MefV1.Primitives.ComposablePartCatalog catalog)
        {
            Requires.NotNull(catalog, "catalog");
            var container = new DebuggableCompositionContainer(catalog, MefV1.Hosting.CompositionOptions.ExportCompositionService | MefV1.Hosting.CompositionOptions.IsThreadSafe);
            return new V1ContainerWrapper(container);
        }

        internal static IContainer CreateContainerV2(params Type[] parts)
        {
            var configuration = new ContainerConfiguration().WithParts(parts);
            return CreateContainerV2(configuration);
        }

        internal static IContainer CreateContainerV2(IReadOnlyList<Assembly> assemblies, Type[] types)
        {
            var configuration = new ContainerConfiguration().WithAssemblies(assemblies).WithParts(types);
            return CreateContainerV2(configuration);
        }

        private static IContainer CreateContainerV2(ContainerConfiguration configuration)
        {
            try
            {
                var container = configuration.CreateContainer();
                return new V2ContainerWrapper(container);
            }
            catch (System.Composition.Hosting.CompositionFailedException ex)
            {
                throw new CompositionFailedException(ex.Message, ex);
            }
        }

        internal static Task<IContainer> CreateContainerV3Async(ITestOutputHelper output, params Type[] parts)
        {
            return CreateContainerV3Async(parts, CompositionEngines.Unspecified, output);
        }

        internal static Task<IContainer> CreateContainerV3Async(IReadOnlyList<Assembly> assemblies, ITestOutputHelper output)
        {
            return CreateContainerV3Async(assemblies, CompositionEngines.Unspecified, output);
        }

        internal static Task<IContainer> CreateContainerV3Async(Type[] parts, CompositionEngines attributesDiscovery, ITestOutputHelper output)
        {
            return CreateContainerV3Async(default(IReadOnlyList<Assembly>), attributesDiscovery, output, parts);
        }

        internal static async Task<IContainer> CreateContainerV3Async(IReadOnlyList<Assembly> assemblies, CompositionEngines attributesDiscovery, ITestOutputHelper output, Type[] parts = null)
        {
            PartDiscovery discovery = GetDiscoveryService(attributesDiscovery);
            var assemblyParts = await discovery.CreatePartsAsync(assemblies);
            var catalog = ComposableCatalog.Create(assemblyParts);
            if (parts != null && parts.Length != 0)
            {
                var typeCatalog = ComposableCatalog.Create(await discovery.CreatePartsAsync(parts));
                catalog = ComposableCatalog.Create(catalog.Parts.Concat(typeCatalog.Parts));
            }

            return await CreateContainerV3Async(catalog, attributesDiscovery, output);
        }

        private static PartDiscovery GetDiscoveryService(CompositionEngines attributesDiscovery)
        {
            var discovery = new List<PartDiscovery>(2);
            if (attributesDiscovery.HasFlag(CompositionEngines.V1))
            {
                discovery.Add(new AttributedPartDiscoveryV1());
            }

            if (attributesDiscovery.HasFlag(CompositionEngines.V2))
            {
                var v2Discovery = new AttributedPartDiscovery();
                if (attributesDiscovery.HasFlag(CompositionEngines.V3NonPublicSupport))
                {
                    v2Discovery.IsNonPublicSupported = true;
                }

                discovery.Add(v2Discovery);
            }

            return PartDiscovery.Combine(discovery.ToArray());
        }

        private static async Task<IContainer> CreateContainerV3Async(ComposableCatalog catalog, CompositionEngines options, ITestOutputHelper output)
        {
            Requires.NotNull(catalog, nameof(catalog));
            Requires.NotNull(output, nameof(output));

            var catalogWithCompositionService = catalog
                .WithCompositionService()
                .WithDesktopSupport();
            var configuration = CompositionConfiguration.Create(catalogWithCompositionService);
            if (!options.HasFlag(CompositionEngines.V3AllowConfigurationWithErrors))
            {
                configuration.ThrowOnErrors();
            }

#if DGML
            string dgmlFile = System.IO.Path.GetTempFileName() + ".dgml";
            configuration.CreateDgml().Save(dgmlFile);
            output.WriteLine("DGML saved to: " + dgmlFile);
#endif
            var container = await configuration.CreateContainerAsync(true, output);
            return new V3ContainerWrapper(container, configuration);
        }

        internal static async Task<RunSummary> RunMultiEngineTest(CompositionEngines attributesVersion, Type[] parts, Func<IContainer, Task<RunSummary>> test, ITestOutputHelper output)
        {
            Requires.NotNull(output, nameof(output));

            var totalSummary = new RunSummary();
            if (attributesVersion.HasFlag(CompositionEngines.V1))
            {
                totalSummary.Aggregate(await test(CreateContainerV1(parts)));
            }

            if (attributesVersion.HasFlag(CompositionEngines.V3EmulatingV1))
            {
                totalSummary.Aggregate(await test(await CreateContainerV3Async(parts, CompositionEngines.V1, output)));
            }

            if (attributesVersion.HasFlag(CompositionEngines.V2))
            {
                totalSummary.Aggregate(await test(CreateContainerV2(parts)));
            }

            if (attributesVersion.HasFlag(CompositionEngines.V3EmulatingV2))
            {
                totalSummary.Aggregate(await test(await CreateContainerV3Async(parts, CompositionEngines.V2, output)));
            }

            if (attributesVersion.HasFlag(CompositionEngines.V3EmulatingV1AndV2AtOnce))
            {
                totalSummary.Aggregate(await test(await CreateContainerV3Async(parts, CompositionEngines.V1 | CompositionEngines.V2, output)));
            }

            return totalSummary;
        }

        internal class DebuggableCompositionContainer : MefV1.Hosting.CompositionContainer
        {
            protected override IEnumerable<MefV1.Primitives.Export> GetExportsCore(MefV1.Primitives.ImportDefinition definition, MefV1.Hosting.AtomicComposition atomicComposition)
            {
                var result = base.GetExportsCore(definition, atomicComposition);
                if ((definition.Cardinality == MefV1.Primitives.ImportCardinality.ExactlyOne && result.Count() != 1) ||
                    (definition.Cardinality == MefV1.Primitives.ImportCardinality.ZeroOrOne && result.Count() > 1))
                {
                    // Set breakpoint here
                }

                return result;
            }

            public DebuggableCompositionContainer(MefV1.Primitives.ComposablePartCatalog catalog, MefV1.Hosting.CompositionOptions compositionOptions)
                : base(catalog, compositionOptions)
            {
            }
        }

        internal class V1ContainerWrapper : IContainer
        {
            private readonly MefV1.Hosting.CompositionContainer container;

            internal MefV1.Hosting.CompositionContainer Container
            {
                get { return container; }
            }

            internal V1ContainerWrapper(MefV1.Hosting.CompositionContainer container)
            {
                Requires.NotNull(container, "container");
                this.container = container;
            }

            public Lazy<T> GetExport<T>()
            {
                try
                {
                    return this.container.GetExport<T>();
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public Lazy<T> GetExport<T>(string contractName)
            {
                try
                {
                    return this.container.GetExport<T>(contractName);
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public Lazy<T, TMetadataView> GetExport<T, TMetadataView>()
            {
                try
                {
                    return this.container.GetExport<T, TMetadataView>();
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public Lazy<T, TMetadataView> GetExport<T, TMetadataView>(string contractName)
            {
                try
                {
                    return this.container.GetExport<T, TMetadataView>(contractName);
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<Lazy<T>> GetExports<T>()
            {
                try
                {
                    return this.container.GetExports<T>();
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<Lazy<T>> GetExports<T>(string contractName)
            {
                try
                {
                    return this.container.GetExports<T>(contractName);
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<Lazy<T, TMetadataView>> GetExports<T, TMetadataView>()
            {
                try
                {
                    return this.container.GetExports<T, TMetadataView>();
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<Lazy<T, TMetadataView>> GetExports<T, TMetadataView>(string contractName)
            {
                try
                {
                    return this.container.GetExports<T, TMetadataView>(contractName);
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public T GetExportedValue<T>()
            {
                try
                {
                    return this.container.GetExportedValue<T>();
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public T GetExportedValue<T>(string contractName)
            {
                try
                {
                    return this.container.GetExportedValue<T>(contractName);
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<T> GetExportedValues<T>()
            {
                try
                {
                    return this.container.GetExportedValues<T>();
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<T> GetExportedValues<T>(string contractName)
            {
                try
                {
                    return this.container.GetExportedValues<T>(contractName);
                }
                catch (MefV1.ImportCardinalityMismatchException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
                catch (MefV1.CompositionException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public void Dispose()
            {
                this.container.Dispose();
            }
        }

        private class V2ContainerWrapper : IContainer
        {
            private readonly CompositionHost container;

            internal V2ContainerWrapper(CompositionHost container)
            {
                Requires.NotNull(container, "container");
                this.container = container;
            }

            public Lazy<T> GetExport<T>()
            {
                // MEF v2 doesn't support this, so emulate it.
                return new Lazy<T>(() =>
                {
                    try
                    {
                        return this.container.GetExport<T>();
                    }
                    catch (System.Composition.Hosting.CompositionFailedException ex)
                    {
                        throw new CompositionFailedException(ex.Message, ex);
                    }
                });
            }

            public Lazy<T> GetExport<T>(string contractName)
            {
                // MEF v2 doesn't support this, so emulate it.
                return new Lazy<T>(() =>
                {
                    try
                    {
                        return this.container.GetExport<T>(contractName);
                    }
                    catch (System.Composition.Hosting.CompositionFailedException ex)
                    {
                        throw new CompositionFailedException(ex.Message, ex);
                    }
                });
            }

            public Lazy<T, TMetadataView> GetExport<T, TMetadataView>()
            {
                throw new NotSupportedException("Not supported by System.Composition.");
            }

            public Lazy<T, TMetadataView> GetExport<T, TMetadataView>(string contractName)
            {
                throw new NotSupportedException("Not supported by System.Composition.");
            }

            public T GetExportedValue<T>()
            {
                try
                {
                    return this.container.GetExport<T>();
                }
                catch (System.Composition.Hosting.CompositionFailedException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public T GetExportedValue<T>(string contractName)
            {
                try
                {
                    return this.container.GetExport<T>(contractName);
                }
                catch (System.Composition.Hosting.CompositionFailedException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<Lazy<T>> GetExports<T>()
            {
                try
                {
                    return this.container.GetExports<T>().Select(v => new Lazy<T>(() => v));
                }
                catch (System.Composition.Hosting.CompositionFailedException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<Lazy<T>> GetExports<T>(string contractName)
            {
                try
                {
                    return this.container.GetExports<T>(contractName).Select(v => new Lazy<T>(() => v));
                }
                catch (System.Composition.Hosting.CompositionFailedException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<Lazy<T, TMetadataView>> GetExports<T, TMetadataView>()
            {
                throw new NotSupportedException();
            }

            public IEnumerable<Lazy<T, TMetadataView>> GetExports<T, TMetadataView>(string contractName)
            {
                throw new NotSupportedException();
            }

            public IEnumerable<T> GetExportedValues<T>()
            {
                try
                {
                    return this.container.GetExports<T>();
                }
                catch (System.Composition.Hosting.CompositionFailedException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public IEnumerable<T> GetExportedValues<T>(string contractName)
            {
                try
                {
                    return this.container.GetExports<T>(contractName);
                }
                catch (System.Composition.Hosting.CompositionFailedException ex)
                {
                    throw new CompositionFailedException(ex.Message, ex);
                }
            }

            public void Dispose()
            {
                this.container.Dispose();
            }
        }

        internal class V3ContainerWrapper : IContainer
        {
            private readonly ExportProvider container;

            internal V3ContainerWrapper(ExportProvider container, CompositionConfiguration configuration)
            {
                Requires.NotNull(container, "container");
                Requires.NotNull(configuration, "configuration");

                this.container = container;
                this.Configuration = configuration;
            }

            internal ExportProvider ExportProvider
            {
                get { return this.container; }
            }

            internal CompositionConfiguration Configuration { get; private set; }

            public Lazy<T> GetExport<T>()
            {
                return this.container.GetExport<T>();
            }

            public Lazy<T> GetExport<T>(string contractName)
            {
                return this.container.GetExport<T>(contractName);
            }

            public Lazy<T, TMetadataView> GetExport<T, TMetadataView>()
            {
                return this.container.GetExport<T, TMetadataView>();
            }

            public Lazy<T, TMetadataView> GetExport<T, TMetadataView>(string contractName)
            {
                return this.container.GetExport<T, TMetadataView>(contractName);
            }

            public T GetExportedValue<T>()
            {
                return this.container.GetExportedValue<T>();
            }

            public T GetExportedValue<T>(string contractName)
            {
                return this.container.GetExportedValue<T>(contractName);
            }

            public IEnumerable<Lazy<T>> GetExports<T>()
            {
                return this.container.GetExports<T>();
            }

            public IEnumerable<Lazy<T>> GetExports<T>(string contractName)
            {
                return this.container.GetExports<T>(contractName);
            }

            public IEnumerable<Lazy<T, TMetadataView>> GetExports<T, TMetadataView>()
            {
                return this.container.GetExports<T, TMetadataView>();
            }

            public IEnumerable<Lazy<T, TMetadataView>> GetExports<T, TMetadataView>(string contractName)
            {
                return this.container.GetExports<T, TMetadataView>(contractName);
            }

            public IEnumerable<T> GetExportedValues<T>()
            {
                return this.container.GetExportedValues<T>();
            }

            public IEnumerable<T> GetExportedValues<T>(string contractName)
            {
                return this.container.GetExportedValues<T>(contractName);
            }

            public void Dispose()
            {
                this.container.Dispose();
            }
        }
    }
}
