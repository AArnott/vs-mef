﻿namespace Microsoft.VisualStudio.Composition.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Composition;
    using System.Composition.Hosting;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Xunit;

    [Trait("SharingBoundary", "")]
    public class SharingBoundaryWithNonNestedBoundaryFactoryTests
    {
        [MefFact(CompositionEngines.V2Compat)]
        public void SharingBoundaryWithNonBoundaryFactory(IContainer container)
        {
            var root = container.GetExportedValue<Root>();
            var boundaryPart = root.SelfFactory.CreateExport().Value;
            var boundaryPartNested = boundaryPart.SelfFactoryWithoutSharingBoundary.CreateExport().Value;

            Assert.Same(boundaryPart, boundaryPartNested);
            Assert.Same(boundaryPart.AnotherSharedValue, boundaryPartNested.AnotherSharedValue);
            Assert.Same(boundaryPart, boundaryPart.AnotherSharedValue.FirstSharedPart);
        }

        [Export]
        public class Root
        {
            [Import, SharingBoundary("A")]
            public ExportFactory<SharingBoundaryPart> SelfFactory { get; set; }
        }

        [Export, Shared("A")]
        public class SharingBoundaryPart
        {
            [Import]
            public ExportFactory<SharingBoundaryPart> SelfFactoryWithoutSharingBoundary { get; set; }

            [Import]
            public AnotherSharedPartInBoundaryA AnotherSharedValue { get; set; }
        }

        [Export, Shared("A")]
        public class AnotherSharedPartInBoundaryA
        {
            [Import]
            public SharingBoundaryPart FirstSharedPart { get; set; }
        }
    }
}