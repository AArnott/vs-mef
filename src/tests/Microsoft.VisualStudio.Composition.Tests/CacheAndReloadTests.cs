﻿// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.VisualStudio.Composition.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Composition;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.Composition.AppDomainTests;
    using Microsoft.VisualStudio.Composition.AppDomainTests2;
    using Xunit;

    public abstract class CacheAndReloadTests
    {
        private ICompositionCacheManager cacheManager;

        protected CacheAndReloadTests(ICompositionCacheManager cacheManager)
        {
            Requires.NotNull(cacheManager, nameof(cacheManager));
            this.cacheManager = cacheManager;
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CacheAndReload(bool stabilizeCatalog)
        {
            var catalog = TestUtilities.EmptyCatalog.AddParts(new[] { TestUtilities.V2Discovery.CreatePart(typeof(SomeExport)) });
            if (stabilizeCatalog)
            {
#if NET452
                var cachedCatalog = new CachedCatalog();
                catalog = cachedCatalog.Stabilize(catalog);
#else
                throw new SkipException("Not applicable on .NET Core");
#endif
            }

            var configuration = CompositionConfiguration.Create(catalog);
            var ms = new MemoryStream();
            await this.cacheManager.SaveAsync(configuration, ms);
            configuration = null;

            ms.Position = 0;
            var exportProviderFactory = await this.cacheManager.LoadExportProviderFactoryAsync(ms, TestUtilities.Resolver);
            var container = exportProviderFactory.CreateExportProvider();
            SomeExport export = container.GetExportedValue<SomeExport>();
            Assert.NotNull(export);
        }

        [Export]
        public class SomeExport { }
    }
}
