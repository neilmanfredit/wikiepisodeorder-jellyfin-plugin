using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Jellyfin.Plugin.WikipediaEpisodeOrder.Services.EpisodeMatcher;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Tests
{
    public class MatchingTests
    {
        private readonly EpisodeMatcher _matcher;

        public MatchingTests()
        {
            _matcher = new EpisodeMatcher(NullLogger.Instance);
        }

        private static JellyfinCandidate C(string title, DateTime? date = null) =>
            new JellyfinCandidate(Guid.NewGuid(), title, date);

        private static WikiEpisode W(string title, DateTime? airDate = null) =>
            new WikiEpisode { Title = title, AirDate = airDate };

        // ─── Level 1: Exact title ─────────────────────────────────────────────────

        [Fact]
        public void Match_ExactTitle_Returns100Confidence()
        {
            var wiki = new List<WikiEpisode> { W("Big Brother") };
            var candidates = new[] { C("Big Brother"), C("Go West Young Man") };

            var results = _matcher.Match(wiki, candidates);

            Assert.Single(results);
            Assert.True(results[0].Matched);
            Assert.Equal(100, results[0].Confidence);
            Assert.Equal("ExactTitle", results[0].MatchMethod);
            Assert.Equal("Big Brother", results[0].JellyfinTitle);
        }

        [Fact]
        public void Match_ExactTitle_IsCaseSensitive()
        {
            // Exact match should prefer same case; normalised match should catch different case
            var wiki = new List<WikiEpisode> { W("big brother") };
            var candidates = new[] { C("big brother") };

            var results = _matcher.Match(wiki, candidates);

            Assert.True(results[0].Matched);
            Assert.Equal(100, results[0].Confidence);
        }

        // ─── Level 2: Normalised title ────────────────────────────────────────────

        [Fact]
        public void Match_NormalisedTitle_IgnoresPunctuation()
        {
            var wiki = new List<WikiEpisode> { W("It's Only a Model") };
            var candidates = new[] { C("Its Only a Model") };

            var results = _matcher.Match(wiki, candidates);

            Assert.True(results[0].Matched);
            Assert.Equal(95, results[0].Confidence);
            Assert.Equal("NormalizedTitle", results[0].MatchMethod);
        }

        [Fact]
        public void Match_NormalisedTitle_IgnoresDashes()
        {
            var wiki = new List<WikiEpisode> { W("Multi-Part Episode") };
            var candidates = new[] { C("MultiPart Episode") };

            var results = _matcher.Match(wiki, candidates);

            Assert.True(results[0].Matched);
            Assert.Equal(95, results[0].Confidence);
            Assert.Equal("NormalizedTitle", results[0].MatchMethod);
        }

        [Fact]
        public void Match_NormalisedTitle_IsCaseInsensitive()
        {
            var wiki = new List<WikiEpisode> { W("THE PILOT") };
            var candidates = new[] { C("the pilot") };

            var results = _matcher.Match(wiki, candidates);

            Assert.True(results[0].Matched);
            Assert.Equal("NormalizedTitle", results[0].MatchMethod);
        }

        [Fact]
        public void Match_NormalisedTitle_RemovesCommasAndPeriods()
        {
            var wiki = new List<WikiEpisode> { W("Hello, World.") };
            var candidates = new[] { C("Hello World") };

            var results = _matcher.Match(wiki, candidates);

            Assert.True(results[0].Matched);
            Assert.Equal("NormalizedTitle", results[0].MatchMethod);
        }

        // ─── Level 3: Air date ────────────────────────────────────────────────────

        [Fact]
        public void Match_AirDate_Returns90Confidence()
        {
            var airDate = new DateTime(1981, 9, 8);
            var wiki = new List<WikiEpisode> { W("Completly Different Title", airDate) };
            var candidates = new[] { C("Big Brother", airDate) };

            var results = _matcher.Match(wiki, candidates);

            Assert.True(results[0].Matched);
            Assert.Equal(90, results[0].Confidence);
            Assert.Equal("AirDate", results[0].MatchMethod);
        }

        [Fact]
        public void Match_AirDate_IgnoresTimePart()
        {
            var wikiDate = new DateTime(2005, 3, 26, 0, 0, 0);
            var jellyfinDate = new DateTime(2005, 3, 26, 19, 0, 0); // time differs
            var wiki = new List<WikiEpisode> { W("Rose", wikiDate) };
            var candidates = new[] { C("Rose Episode Different Name", jellyfinDate) };

            var results = _matcher.Match(wiki, candidates);

            // Exact and normalised won't match, air date should
            Assert.True(results[0].Matched);
            Assert.Equal("AirDate", results[0].MatchMethod);
        }

        [Fact]
        public void Match_AirDate_NotUsedWhenNullOnWikiEpisode()
        {
            var wiki = new List<WikiEpisode> { W("Completely Different") };
            var candidates = new[] { C("Something Else", new DateTime(2000, 1, 1)) };

            var results = _matcher.Match(wiki, candidates);

            // No air date on wiki side, so air date matching won't trigger
            Assert.False(results[0].Matched);
        }

        // ─── Level 4: Fuzzy ───────────────────────────────────────────────────────

        [Fact]
        public void Match_FuzzyTitle_AboveThreshold_Matches()
        {
            // Small typo — should exceed 90% fuzzy ratio
            var wiki = new List<WikiEpisode> { W("The Begining") }; // 'beginning' typo
            var candidates = new[] { C("The Beginning") };

            var results = _matcher.Match(wiki, candidates);

            Assert.True(results[0].Matched);
            Assert.Equal("FuzzyTitle", results[0].MatchMethod);
            Assert.True(results[0].Confidence >= 90);
        }

        [Fact]
        public void Match_FuzzyTitle_BelowThreshold_NoMatch()
        {
            var wiki = new List<WikiEpisode> { W("Alpha") };
            var candidates = new[] { C("Completely Different Title Here") };

            var results = _matcher.Match(wiki, candidates);

            Assert.False(results[0].Matched);
        }

        // ─── Match uniqueness ─────────────────────────────────────────────────────

        [Fact]
        public void Match_EachJellyfinItemUsedOnce()
        {
            var sharedCandidate = C("Pilot");
            var wiki = new List<WikiEpisode> { W("Pilot"), W("Pilot") }; // two episodes with same title
            var candidates = new[] { sharedCandidate, C("Other") };

            var results = _matcher.Match(wiki, candidates);

            Assert.Equal(2, results.Count);
            // First should match, second cannot reuse the same Jellyfin item
            Assert.True(results[0].Matched);
            Assert.False(results[1].Matched);
        }

        // ─── Unmatched episodes ───────────────────────────────────────────────────

        [Fact]
        public void Match_UnmatchedEpisode_HasMatchedFalse()
        {
            var wiki = new List<WikiEpisode> { W("Non-Existent Episode") };
            var candidates = new[] { C("Something Completely Different XYZ ABC") };

            var results = _matcher.Match(wiki, candidates);

            Assert.Single(results);
            Assert.False(results[0].Matched);
            Assert.Equal("None", results[0].MatchMethod);
        }

        [Fact]
        public void Match_EmptyCandidateList_AllUnmatched()
        {
            var wiki = new List<WikiEpisode> { W("Episode 1"), W("Episode 2") };

            var results = _matcher.Match(wiki, Array.Empty<JellyfinCandidate>());

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.False(r.Matched));
        }

        [Fact]
        public void Match_EmptyWikiList_ReturnsEmpty()
        {
            var candidates = new[] { C("Some Episode") };

            var results = _matcher.Match(new List<WikiEpisode>(), candidates);

            Assert.Empty(results);
        }

        // ─── Title normalisation ──────────────────────────────────────────────────

        [Theory]
        [InlineData("It's a Trap!", "its a trap")]
        [InlineData("Hello, World.", "hello world")]
        [InlineData("Multi-Part", "multipart")]
        [InlineData("\"Quoted\"", "quoted")]
        [InlineData("Em—Dash", "emdash")]
        [InlineData("THE EPISODE", "the episode")]
        public void NormalizeTitle_StripsExpectedCharacters(string input, string expected)
        {
            Assert.Equal(expected, EpisodeMatcher.NormalizeTitle(input));
        }

        [Fact]
        public void NormalizeTitle_EmptyStringReturnsEmpty()
        {
            Assert.Equal(string.Empty, EpisodeMatcher.NormalizeTitle(string.Empty));
        }

        // ─── Multi-episode matching ───────────────────────────────────────────────

        [Fact]
        public void Match_MultipleEpisodes_AllMatchedInOrder()
        {
            var wiki = new List<WikiEpisode>
            {
                W("Space Pilot 3000"),
                W("The Series Has Landed"),
                W("I, Roommate")
            };
            var candidates = new[]
            {
                C("I, Roommate"),
                C("Space Pilot 3000"),
                C("The Series Has Landed")
            };

            var results = _matcher.Match(wiki, candidates);

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.True(r.Matched));
            Assert.Equal("Space Pilot 3000", results[0].JellyfinTitle);
            Assert.Equal("The Series Has Landed", results[1].JellyfinTitle);
            Assert.Equal("I, Roommate", results[2].JellyfinTitle);
        }

        [Fact]
        public void Match_WikiEpisodePreservedInResult()
        {
            var wikiEp = new WikiEpisode { Order = 5, Title = "Pilot", IsSpecial = false };
            var results = _matcher.Match(
                new List<WikiEpisode> { wikiEp },
                new[] { C("Pilot") });

            Assert.Same(wikiEp, results[0].WikiEpisode);
        }
    }
}
