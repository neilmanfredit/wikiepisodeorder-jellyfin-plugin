using System.Collections.Generic;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            Mappings = new List<SeriesMapping>();
        }

        public List<SeriesMapping> Mappings { get; set; }
    }
}
