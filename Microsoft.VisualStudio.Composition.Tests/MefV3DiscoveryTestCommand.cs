﻿namespace Microsoft.VisualStudio.Composition.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using Validation;
    using Xunit;
    using Xunit.Sdk;

    public class MefV3DiscoveryTestCommand : FactCommand
    {
        private readonly CompositionEngines compositionVersions;
        private readonly bool expectInvalidConfiguration;
        private readonly Type[] parts;
        private readonly IReadOnlyList<string> assemblyNames;

        public MefV3DiscoveryTestCommand(IMethodInfo method, CompositionEngines compositionEngines, Type[] parts, IReadOnlyList<string> assemblyNames, bool expectInvalidConfiguration)
            : base(method)
        {
            Requires.NotNull(method, "method");
            Requires.NotNull(parts, "parts");
            Requires.NotNull(assemblyNames, "assemblyNames");

            this.compositionVersions = compositionEngines;
            this.assemblyNames = assemblyNames;
            this.parts = parts;
            this.expectInvalidConfiguration = expectInvalidConfiguration;

            this.DisplayName = "V3 composition";
        }

        public MethodResult Result { get; set; }

        public CompositionConfiguration ResultingConfiguration { get; set; }

        public override MethodResult Execute(object testClass)
        {
            try
            {
                var v3DiscoveryModules = this.GetV3DiscoveryModules();

                var resultingCatalogs = new List<ComposableCatalog>(v3DiscoveryModules.Count);

                var assemblies = this.assemblyNames.Select(Assembly.Load).ToList();
                foreach (var discoveryModule in v3DiscoveryModules)
                {
                    var partsFromTypes = discoveryModule.CreatePartsAsync(this.parts).GetAwaiter().GetResult();
                    var partsFromAssemblies = discoveryModule.CreatePartsAsync(assemblies).GetAwaiter().GetResult();
                    var catalog = ComposableCatalog.Create()
                        .WithParts(partsFromTypes)
                        .WithParts(partsFromAssemblies);
                    resultingCatalogs.Add(catalog);
                }

                // Verify that the catalogs are identical.
                for (int i = 1; i < resultingCatalogs.Count; i++)
                {
                    Assert.Equal(resultingCatalogs[i - 1], resultingCatalogs[i]);
                }

                // Now that we've proven that all part discovery mechanisms produced identical catalogs,
                // create one configuration and verify it meets expectations.
                var catalogWithSupport = resultingCatalogs[0]
                    .WithCompositionService()
                    .WithDesktopSupport();
                var configuration = CompositionConfiguration.Create(catalogWithSupport);

                if (!this.compositionVersions.HasFlag(CompositionEngines.V3AllowConfigurationWithErrors))
                {
                    Assert.Equal(this.expectInvalidConfiguration, !configuration.CompositionErrors.IsEmpty || !catalogWithSupport.DiscoveredParts.DiscoveryErrors.IsEmpty);
                }

                // Save the configuration in a property so that the engine test that follows can reuse the work we've done.
                this.ResultingConfiguration = configuration;

                return this.Result = new PassedResult(this.testMethod, this.DisplayName);
            }
            catch (Exception ex)
            {
                return this.Result = new FailedResult(this.testMethod, ex, this.DisplayName);
            }
        }

        private IReadOnlyCollection<PartDiscovery> GetV3DiscoveryModules()
        {
            var titleAppends = new List<string>();

            var discovery = new List<PartDiscovery>();
            if (this.compositionVersions.HasFlag(CompositionEngines.V3EmulatingV1))
            {
                discovery.Add(new AttributedPartDiscoveryV1());
                titleAppends.Add("V1");
            }

            if (this.compositionVersions.HasFlag(CompositionEngines.V3EmulatingV2))
            {
                discovery.Add(new AttributedPartDiscovery { IsNonPublicSupported = compositionVersions.HasFlag(CompositionEngines.V3EmulatingV2WithNonPublic) });
                titleAppends.Add("V2");
            }

            if (this.compositionVersions.HasFlag(CompositionEngines.V3EmulatingV1AndV2AtOnce))
            {
                discovery.Add(PartDiscovery.Combine(
                    new AttributedPartDiscoveryV1(),
                    new AttributedPartDiscovery { IsNonPublicSupported = compositionVersions.HasFlag(CompositionEngines.V3EmulatingV2WithNonPublic) }));
                titleAppends.Add("V1+V2");
            }

            this.DisplayName += " (" + string.Join(", ", titleAppends) + ")";

            return discovery;
        }
    }
}
