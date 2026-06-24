using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin? Instance { get; private set; }

        public override string Name => "Wikipedia Episode Order";

        public override Guid Id => new Guid("a8cba4a4-65b8-4a7e-9f0e-b3e56b6e3b7b");

        public override string Description =>
            "Uses Wikipedia episode lists to define TV show playback ordering, correctly placing specials and TV movies.";

        public IEnumerable<PluginPageInfo> GetPages()
        {
            var ns = GetType().Namespace;
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "WikipediaEpisodeOrder",
                    EmbeddedResourcePath = $"{ns}.Web.configPage.html"
                },
                new PluginPageInfo
                {
                    Name = "WikipediaEpisodeOrderJS",
                    EmbeddedResourcePath = $"{ns}.Web.configPage.js",
                    EnableInMainMenu = false
                },
                new PluginPageInfo
                {
                    Name = "WikipediaEpisodeOrderPreview",
                    EmbeddedResourcePath = $"{ns}.Web.orderPreview.html"
                },
                new PluginPageInfo
                {
                    Name = "WikipediaEpisodeOrderPreviewJS",
                    EmbeddedResourcePath = $"{ns}.Web.orderPreview.js",
                    EnableInMainMenu = false
                }
            };
        }
    }
}
