using Jellyfin.Plugin.WikipediaEpisodeOrder.Providers;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<CacheService>(sp => new CacheService(
                sp.GetRequiredService<IApplicationPaths>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<CacheService>()));

            serviceCollection.AddSingleton<WikipediaEpisodeProvider>();

            serviceCollection.AddSingleton<EpisodeMatcher>(sp => new EpisodeMatcher(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<EpisodeMatcher>()));

            serviceCollection.AddSingleton<OrderBuilderService>(sp => new OrderBuilderService(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<OrderBuilderService>()));

            serviceCollection.AddSingleton<RefreshService>();

            serviceCollection.AddSingleton<PlaybackOrderService>();
        }
    }
}
