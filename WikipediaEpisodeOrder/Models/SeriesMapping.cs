using System;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Models
{
    public class SeriesMapping
    {
        public Guid SeriesId { get; set; }

        public string SeriesName { get; set; } = string.Empty;

        public string WikipediaUrl { get; set; } = string.Empty;

        public bool AutoRefresh { get; set; }

        public int RefreshDays { get; set; } = 7;

        public DateTime LastUpdatedUtc { get; set; }
    }
}
