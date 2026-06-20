using System;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Models
{
    public class WikiEpisode
    {
        public int Order { get; set; }

        public string Title { get; set; } = string.Empty;

        public int? Season { get; set; }

        public int? EpisodeNumber { get; set; }

        public DateTime? AirDate { get; set; }

        public string? ProductionCode { get; set; }

        public bool IsSpecial { get; set; }

        public string? SourceSection { get; set; }
    }
}
