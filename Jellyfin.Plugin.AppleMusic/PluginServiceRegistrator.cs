using System;
using System.IO;
using System.Net.Http;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using Jellyfin.Plugin.AppleMusic.Catalog.Throttling;
using Jellyfin.Plugin.AppleMusic.Organizer;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic;

/// <summary>
/// Registers the catalog layer so the providers can take it by constructor
/// injection.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Singleton so the web player token is fetched once and shared, rather
        // than re-scraped per request.
        serviceCollection.AddSingleton<IWebPlayTokenProvider>(provider => new WebPlayTokenProvider(
            CreateHttpClient(provider),
            provider.GetRequiredService<ILogger<WebPlayTokenProvider>>()));

        serviceCollection.AddSingleton<ICatalogCache>(provider => new CatalogCache(
            CacheRoot(provider.GetRequiredService<IApplicationPaths>()),
            CurrentCacheOptions,
            provider.GetRequiredService<ILogger<CatalogCache>>()));

        // Cache -> throttle -> network. The cache sits outside so hits are not
        // paced; the throttle sits outside the network so every real request
        // is, including the ones the cache issues for misses.
        serviceCollection.AddSingleton<ICatalogTransport>(provider => new CachingCatalogTransport(
            new ThrottledCatalogTransport(
                new WebPlayTransport(
                    CreateHttpClient(provider),
                    provider.GetRequiredService<IWebPlayTokenProvider>(),
                    provider.GetRequiredService<ILogger<WebPlayTransport>>()),
                CurrentThrottleOptions,
                provider.GetRequiredService<ILogger<ThrottledCatalogTransport>>()),
            provider.GetRequiredService<ICatalogCache>(),
            provider.GetRequiredService<ILogger<CachingCatalogTransport>>()));

        serviceCollection.AddSingleton<IAppleMusicCatalog>(provider => new AppleMusicCatalog(
            provider.GetRequiredService<ICatalogTransport>(),
            CurrentOptions,
            provider.GetRequiredService<ILogger<AppleMusicCatalog>>()));

        serviceCollection.AddSingleton(provider => new LibraryOrganizer(
            provider.GetRequiredService<ILibraryManager>(),
            provider.GetRequiredService<IAppleMusicCatalog>(),
            CurrentOrganizeOptions,
            provider.GetRequiredService<ILogger<LibraryOrganizer>>()));
    }

    /// <summary>
    /// Reads the options afresh on every call, so changes made on the
    /// configuration page take effect without a server restart.
    /// </summary>
    /// <returns>The current catalog options.</returns>
    private static CatalogOptions CurrentOptions()
        => Plugin.Instance?.Configuration.ToCatalogOptions() ?? new CatalogOptions();

    private static OrganizeOptions CurrentOrganizeOptions()
        => Plugin.Instance?.Configuration.ToOrganizeOptions() ?? new OrganizeOptions();

    private static ThrottleOptions CurrentThrottleOptions()
        => Plugin.Instance?.Configuration.ToThrottleOptions() ?? new ThrottleOptions();

    private static CatalogCacheOptions CurrentCacheOptions()
        => Plugin.Instance?.Configuration.ToCacheOptions() ?? new CatalogCacheOptions();

    private static string CacheRoot(IApplicationPaths paths)
        => Path.Combine(paths.CachePath, "apple-music");

    private static HttpClient CreateHttpClient(IServiceProvider provider)
        => provider.GetRequiredService<IHttpClientFactory>().CreateClient(NamedClient.Default);
}
