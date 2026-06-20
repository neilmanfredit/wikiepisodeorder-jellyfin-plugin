using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Services
{
    public class OrderBuilderService
    {
        private readonly ILogger _logger;

        public OrderBuilderService(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Produces a canonical playback sequence from matched episodes.
        /// Wikipedia order always wins — no secondary sorting by season, episode number, or production code.
        /// Unmatched episodes are included as gaps (no Jellyfin ID) so callers can report them.
        /// </summary>
        public IReadOnlyList<PlaybackEntry> Build(IEnumerable<MatchedEpisode> matchedEpisodes)
        {
            var ordered = matchedEpisodes
                .OrderBy(m => m.WikiEpisode.Order)
                .ToList();

            var entries = new List<PlaybackEntry>(ordered.Count);

            foreach (var match in ordered)
            {
                entries.Add(new PlaybackEntry(
                    Position: entries.Count + 1,
                    WikiOrder: match.WikiEpisode.Order,
                    WikiTitle: match.WikiEpisode.Title,
                    IsSpecial: match.WikiEpisode.IsSpecial,
                    Matched: match.Matched,
                    JellyfinItemId: match.Matched ? match.JellyfinItemId : (Guid?)null,
                    JellyfinTitle: match.Matched ? match.JellyfinTitle : null,
                    Confidence: match.Confidence,
                    MatchMethod: match.MatchMethod
                ));

                if (!match.Matched)
                {
                    _logger.LogWarning(
                        "Unmatched wiki episode at position {Position}: '{Title}' (special={IsSpecial})",
                        entries.Count, match.WikiEpisode.Title, match.WikiEpisode.IsSpecial);
                }
            }

            int matched = entries.Count(e => e.Matched);
            _logger.LogInformation(
                "Playback order built: {Total} entries, {Matched} matched, {Unmatched} unmatched",
                entries.Count, matched, entries.Count - matched);

            return entries;
        }

        /// <summary>
        /// Returns only the Jellyfin item IDs in playback order, skipping unmatched entries.
        /// </summary>
        public IReadOnlyList<Guid> BuildPlayQueue(IEnumerable<MatchedEpisode> matchedEpisodes)
        {
            return Build(matchedEpisodes)
                .Where(e => e.Matched && e.JellyfinItemId.HasValue)
                .Select(e => e.JellyfinItemId!.Value)
                .ToList();
        }
    }

    public record PlaybackEntry(
        int Position,
        int WikiOrder,
        string WikiTitle,
        bool IsSpecial,
        bool Matched,
        Guid? JellyfinItemId,
        string? JellyfinTitle,
        double Confidence,
        string MatchMethod);
}
