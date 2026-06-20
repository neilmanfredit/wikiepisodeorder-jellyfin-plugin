using System;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Models
{
    public class MatchedEpisode
    {
        public WikiEpisode WikiEpisode { get; set; } = new WikiEpisode();

        public Guid JellyfinItemId { get; set; }

        public string JellyfinTitle { get; set; } = string.Empty;

        public double Confidence { get; set; }

        public string MatchMethod { get; set; } = string.Empty;

        public bool Matched { get; set; }
    }
}
