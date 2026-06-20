using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Models
{
    public class WikiSeriesOrder
    {
        public string SeriesName { get; set; } = string.Empty;

        public string WikipediaUrl { get; set; } = string.Empty;

        public DateTime LastUpdatedUtc { get; set; }

        public List<WikiEpisode> Episodes { get; set; } = new List<WikiEpisode>();
    }
}
