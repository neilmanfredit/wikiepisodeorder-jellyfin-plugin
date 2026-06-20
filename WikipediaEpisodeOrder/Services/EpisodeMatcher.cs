using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FuzzySharp;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Services
{
    public class EpisodeMatcher
    {
        private const double ExactTitleConfidence = 100;
        private const double NormalizedTitleConfidence = 95;
        private const double AirDateConfidence = 90;
        private const int FuzzyThreshold = 90;

        private readonly ILogger _logger;

        public EpisodeMatcher(ILogger logger)
        {
            _logger = logger;
        }

        public record JellyfinCandidate(Guid Id, string Title, DateTime? PremiereDate);

        public List<MatchedEpisode> Match(
            List<WikiEpisode> wikiEpisodes,
            IEnumerable<JellyfinCandidate> jellyfinItems)
        {
            var candidates = jellyfinItems.ToList();
            var used = new HashSet<Guid>();
            var result = new List<MatchedEpisode>();

            foreach (var wiki in wikiEpisodes)
            {
                var match = TryMatch(wiki, candidates, used);
                result.Add(match);

                if (match.Matched)
                {
                    used.Add(match.JellyfinItemId);
                    _logger.LogDebug(
                        "Matched '{WikiTitle}' → '{JellyfinTitle}' via {Method} ({Confidence}%)",
                        wiki.Title, match.JellyfinTitle, match.MatchMethod, match.Confidence);
                }
                else
                {
                    _logger.LogDebug("No match for wiki episode '{WikiTitle}'", wiki.Title);
                }
            }

            int matched = result.Count(m => m.Matched);
            _logger.LogInformation(
                "Matching complete: {Matched}/{Total} episodes matched",
                matched, result.Count);

            return result;
        }

        private MatchedEpisode TryMatch(
            WikiEpisode wiki,
            List<JellyfinCandidate> candidates,
            HashSet<Guid> used)
        {
            var available = candidates.Where(c => !used.Contains(c.Id)).ToList();

            // Level 1: Exact title
            var exact = available.FirstOrDefault(c =>
                string.Equals(c.Title, wiki.Title, StringComparison.Ordinal));
            if (exact != null)
                return BuildMatch(wiki, exact, ExactTitleConfidence, "ExactTitle");

            // Level 2: Normalised title
            var normWiki = NormalizeTitle(wiki.Title);
            var normalised = available.FirstOrDefault(c =>
                string.Equals(NormalizeTitle(c.Title), normWiki, StringComparison.OrdinalIgnoreCase));
            if (normalised != null)
                return BuildMatch(wiki, normalised, NormalizedTitleConfidence, "NormalizedTitle");

            // Level 3: Air date
            if (wiki.AirDate.HasValue)
            {
                var dateMatch = available.FirstOrDefault(c =>
                    c.PremiereDate.HasValue &&
                    c.PremiereDate.Value.Date == wiki.AirDate.Value.Date);
                if (dateMatch != null)
                    return BuildMatch(wiki, dateMatch, AirDateConfidence, "AirDate");
            }

            // Level 4: Fuzzy title
            JellyfinCandidate? bestFuzzy = null;
            int bestRatio = 0;
            foreach (var c in available)
            {
                int ratio = Fuzz.Ratio(normWiki, NormalizeTitle(c.Title));
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestFuzzy = c;
                }
            }

            if (bestFuzzy != null && bestRatio >= FuzzyThreshold)
                return BuildMatch(wiki, bestFuzzy, bestRatio, "FuzzyTitle");

            // No match
            return new MatchedEpisode
            {
                WikiEpisode = wiki,
                Matched = false,
                Confidence = bestFuzzy != null ? bestRatio : 0,
                MatchMethod = "None"
            };
        }

        private static MatchedEpisode BuildMatch(
            WikiEpisode wiki,
            JellyfinCandidate candidate,
            double confidence,
            string method) =>
            new MatchedEpisode
            {
                WikiEpisode = wiki,
                JellyfinItemId = candidate.Id,
                JellyfinTitle = candidate.Title,
                Confidence = confidence,
                MatchMethod = method,
                Matched = true
            };

        // Remove punctuation/special characters and normalise case for comparison
        public static string NormalizeTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;

            // Remove apostrophes, punctuation, quotes, dashes, periods, commas
            var normalised = Regex.Replace(title, @"['‘’“”"".,\-–—!?:;/\\()]", string.Empty);

            // Collapse whitespace
            normalised = Regex.Replace(normalised, @"\s+", " ").Trim();

            // Lower-case for comparison
            return normalised.ToLowerInvariant();
        }
    }
}
