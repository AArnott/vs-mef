// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.Composition.Tests;

using System.Reflection;
#if NETCOREAPP
using System.Runtime.Loader;
using System.Threading.Tasks;
#endif
using Microsoft.VisualStudio.Composition.AssemblyDiscoveryTests;
using Xunit;

/// <summary>
/// Tests the StandardAssemblyLoader.
/// </summary>
/// <remarks>
/// These tests must test the behavior indirectly because the StandardAssemblyLoader is internal (and should remain so),
/// and we do not allow ourselves to use InternalsVisibleTo (which also should remain so).
/// </remarks>
public class StandardAssemblyLoaderTests
{
#if NETCOREAPP
    /// <summary>
    /// Verifies that the assembly is loaded into the ContextualReflection ALC (rather than the default ALC).
    /// </summary>
    [Fact]
    public async Task LoadAssembly_LoadsIntoContextualReflectionALC()
    {
        AssemblyLoadContext alc = new("test ALC");
        Assembly externalAssembly = alc.LoadFromAssemblyPath(typeof(DiscoverablePart1).Assembly.Location);
        Assert.Same(alc, AssemblyLoadContext.GetLoadContext(externalAssembly)); // test sanity check
        Resolver resolver = Resolver.DefaultInstance;
        using (AssemblyLoadContext.EnterContextualReflection(externalAssembly))
        {
            AttributedPartDiscovery discovery = new(resolver);
            ComposableCatalog catalog = ComposableCatalog.Create(resolver)
                .AddParts(await discovery.CreatePartsAsync(new string[] { typeof(DiscoverablePart1).Assembly.Location }));
            CompositionConfiguration configuration = CompositionConfiguration.Create(catalog);
            IExportProviderFactory factory = configuration.CreateExportProviderFactory();
            ExportProvider exportProvider = factory.CreateExportProvider();

            object value = exportProvider.GetExportedValue<object>(typeof(DiscoverablePart1).FullName);
            Assert.Same(alc, AssemblyLoadContext.GetLoadContext(value.GetType().Assembly));
        }
    }

    /// <summary>
    /// Verifies that the assembly is loaded into the appropriate ALC (rather than the default ALC).
    /// </summary>
    [Fact]
    public void LoadAssembly_LoadsIntoMefALC()
    {
        AssemblyLoadContext alc = new("test ALC");
        alc.Resolving += (s, e) =>
        {
            return null;
        };

        Assembly testAssemblyInAlc = alc.LoadFromAssemblyPath(Assembly.GetExecutingAssembly().Location);
        Assert.Same(alc, AssemblyLoadContext.GetLoadContext(testAssemblyInAlc)); // test sanity check
        MethodInfo helperMethod = testAssemblyInAlc.GetType(typeof(StandardAssemblyLoaderTests).FullName!)!.GetMethod(nameof(LoadAssemblyTestHelper), BindingFlags.Static | BindingFlags.NonPublic)!;
        object value = helperMethod!.Invoke(null, null)!;
        Assert.Same(/*alc*/AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(value.GetType().Assembly));
    }

    private static object LoadAssemblyTestHelper()
    {
        AssemblyLoadContext? alc2 = AssemblyLoadContext.GetLoadContext(typeof(StandardAssemblyLoaderTests).Assembly);
        Resolver resolver = Resolver.DefaultInstance;
        AttributedPartDiscovery discovery = new(resolver);
        ComposableCatalog catalog = ComposableCatalog.Create(resolver)
            .AddPart(discovery.CreatePart(typeof(DiscoverablePart1))!);
        CompositionConfiguration configuration = CompositionConfiguration.Create(catalog);
        IExportProviderFactory factory = configuration.CreateExportProviderFactory();
        ExportProvider exportProvider = factory.CreateExportProvider();

        return exportProvider.GetExportedValue<object>(typeof(DiscoverablePart1).FullName);
    }
#endif
}
