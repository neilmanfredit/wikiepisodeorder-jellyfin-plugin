using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Tests
{
    public class OrderingTests
    {
        private readonly OrderBuilderService _builder;

        public OrderingTests()
        {
            _builder = new OrderBuilderService(NullLogger.Instance);
        }

        private static MatchedEpisode Matched(int wikiOrder, string title, bool isSpecial = false)
        {
            return new MatchedEpisode
            {
                WikiEpisode = new WikiEpisode { Order = wikiOrder, Title = title, IsSpecial = isSpecial },
                JellyfinItemId = Guid.NewGuid(),
                JellyfinTitle = title,
                Confidence = 100,
                MatchMethod = "ExactTitle",
                Matched = true
            };
        }

        private static MatchedEpisode Unmatched(int wikiOrder, string title, bool isSpecial = false)
        {
            return new MatchedEpisode
            {
                WikiEpisode = new WikiEpisode { Order = wikiOrder, Title = title, IsSpecial = isSpecial },
                Matched = false,
                MatchMethod = "None"
            };
        }

        // ─── Wikipedia order preserved ────────────────────────────────────────────

        [Fact]
        public void Build_WikipediaOrderPreserved()
        {
            var episodes = new List<MatchedEpisode>
            {
                Matched(1, "Alpha"),
                Matched(2, "Beta"),
                Matched(3, "Gamma")
            };

            var result = _builder.Build(episodes);

            Assert.Equal(3, result.Count);
            Assert.Equal("Alpha", result[0].WikiTitle);
            Assert.Equal("Beta", result[1].WikiTitle);
            Assert.Equal("Gamma", result[2].WikiTitle);
            Assert.Equal(1, result[0].Position);
            Assert.Equal(2, result[1].Position);
            Assert.Equal(3, result[2].Position);
        }

        [Fact]
        public void Build_PositionIsAlwaysSequential()
        {
            var episodes = new List<MatchedEpisode>
            {
                Matched(10, "Ep10"), Matched(20, "Ep20"), Matched(30, "Ep30")
            };

            var result = _builder.Build(episodes);

            Assert.Equal(1, result[0].Position);
            Assert.Equal(2, result[1].Position);
            Assert.Equal(3, result[2].Position);
        }

        // ─── Special insertion preserved ──────────────────────────────────────────

        [Fact]
        public void Build_SpecialsInsertedAtCorrectPosition()
        {
            // Wikipedia order: ep1, ep2, xmas special, ep3, movie, ep4
            var episodes = new List<MatchedEpisode>
            {
                Matched(1, "Episode 1"),
                Matched(2, "Episode 2"),
                Matched(3, "Christmas Special", isSpecial: true),
                Matched(4, "Episode 3"),
                Matched(5, "Movie", isSpecial: true),
                Matched(6, "Episode 4")
            };

            var result = _builder.Build(episodes);

            Assert.Equal(6, result.Count);
            Assert.False(result[0].IsSpecial);
            Assert.False(result[1].IsSpecial);
            Assert.True(result[2].IsSpecial);   // Christmas Special at position 3
            Assert.Equal("Christmas Special", result[2].WikiTitle);
            Assert.False(result[3].IsSpecial);
            Assert.True(result[4].IsSpecial);   // Movie at position 5
            Assert.False(result[5].IsSpecial);
        }

        // ─── No secondary sorting ─────────────────────────────────────────────────

        [Fact]
        public void Build_DoesNotReorderBySeasonOrEpisodeNumber()
        {
            // Out-of-order season/episode numbers that should NOT be reordered
            var ep1 = new WikiEpisode { Order = 1, Title = "Ep1", Season = 2, EpisodeNumber = 3 };
            var ep2 = new WikiEpisode { Order = 2, Title = "Ep2", Season = 1, EpisodeNumber = 1 };
            var ep3 = new WikiEpisode { Order = 3, Title = "Ep3", Season = 2, EpisodeNumber = 1 };

            var episodes = new List<MatchedEpisode>
            {
                new MatchedEpisode { WikiEpisode = ep1, JellyfinItemId = Guid.NewGuid(), JellyfinTitle = "Ep1", Confidence = 100, MatchMethod = "ExactTitle", Matched = true },
                new MatchedEpisode { WikiEpisode = ep2, JellyfinItemId = Guid.NewGuid(), JellyfinTitle = "Ep2", Confidence = 100, MatchMethod = "ExactTitle", Matched = true },
                new MatchedEpisode { WikiEpisode = ep3, JellyfinItemId = Guid.NewGuid(), JellyfinTitle = "Ep3", Confidence = 100, MatchMethod = "ExactTitle", Matched = true }
            };

            var result = _builder.Build(episodes);

            // Wikipedia order (1,2,3) must be maintained regardless of season/episode numbers
            Assert.Equal("Ep1", result[0].WikiTitle);
            Assert.Equal("Ep2", result[1].WikiTitle);
            Assert.Equal("Ep3", result[2].WikiTitle);
        }

        [Fact]
        public void Build_DoesNotReorderByProductionCode()
        {
            var ep1 = new WikiEpisode { Order = 1, Title = "Ep1", ProductionCode = "C" };
            var ep2 = new WikiEpisode { Order = 2, Title = "Ep2", ProductionCode = "A" };
            var ep3 = new WikiEpisode { Order = 3, Title = "Ep3", ProductionCode = "B" };

            var episodes = new List<MatchedEpisode>
            {
                new MatchedEpisode { WikiEpisode = ep1, JellyfinItemId = Guid.NewGuid(), JellyfinTitle = "Ep1", Confidence = 100, MatchMethod = "ExactTitle", Matched = true },
                new MatchedEpisode { WikiEpisode = ep2, JellyfinItemId = Guid.NewGuid(), JellyfinTitle = "Ep2", Confidence = 100, MatchMethod = "ExactTitle", Matched = true },
                new MatchedEpisode { WikiEpisode = ep3, JellyfinItemId = Guid.NewGuid(), JellyfinTitle = "Ep3", Confidence = 100, MatchMethod = "ExactTitle", Matched = true }
            };

            var result = _builder.Build(episodes);

            Assert.Equal("Ep1", result[0].WikiTitle);
            Assert.Equal("Ep2", result[1].WikiTitle);
            Assert.Equal("Ep3", result[2].WikiTitle);
        }

        // ─── Unmatched episodes ───────────────────────────────────────────────────

        [Fact]
        public void Build_UnmatchedEpisodesIncludedAsGaps()
        {
            var episodes = new List<MatchedEpisode>
            {
                Matched(1, "Matched Episode"),
                Unmatched(2, "Missing Episode"),
                Matched(3, "Also Matched")
            };

            var result = _builder.Build(episodes);

            Assert.Equal(3, result.Count);
            Assert.True(result[0].Matched);
            Assert.False(result[1].Matched);
            Assert.Null(result[1].JellyfinItemId);
            Assert.True(result[2].Matched);
        }

        [Fact]
        public void BuildPlayQueue_SkipsUnmatchedEpisodes()
        {
            var episodes = new List<MatchedEpisode>
            {
                Matched(1, "Episode 1"),
                Unmatched(2, "Missing"),
                Matched(3, "Episode 3"),
                Unmatched(4, "Also Missing"),
                Matched(5, "Episode 5")
            };

            var queue = _builder.BuildPlayQueue(episodes);

            Assert.Equal(3, queue.Count);
        }

        [Fact]
        public void BuildPlayQueue_ReturnsIdsInWikipediaOrder()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();

            var episodes = new List<MatchedEpisode>
            {
                new MatchedEpisode { WikiEpisode = new WikiEpisode { Order = 1 }, JellyfinItemId = id1, Matched = true, MatchMethod = "ExactTitle" },
                new MatchedEpisode { WikiEpisode = new WikiEpisode { Order = 2 }, JellyfinItemId = id2, Matched = true, MatchMethod = "ExactTitle" },
                new MatchedEpisode { WikiEpisode = new WikiEpisode { Order = 3 }, JellyfinItemId = id3, Matched = true, MatchMethod = "ExactTitle" }
            };

            var queue = _builder.BuildPlayQueue(episodes);

            Assert.Equal(id1, queue[0]);
            Assert.Equal(id2, queue[1]);
            Assert.Equal(id3, queue[2]);
        }

        // ─── Edge cases ───────────────────────────────────────────────────────────

        [Fact]
        public void Build_EmptyInput_ReturnsEmpty()
        {
            var result = _builder.Build(new List<MatchedEpisode>());
            Assert.Empty(result);
        }

        [Fact]
        public void Build_AllUnmatched_StillPreservesOrder()
        {
            var episodes = new List<MatchedEpisode>
            {
                Unmatched(1, "Missing 1"),
                Unmatched(2, "Missing 2"),
                Unmatched(3, "Missing 3")
            };

            var result = _builder.Build(episodes);

            Assert.Equal(3, result.Count);
            Assert.All(result, e => Assert.False(e.Matched));
            Assert.Equal("Missing 1", result[0].WikiTitle);
            Assert.Equal("Missing 3", result[2].WikiTitle);
        }

        [Fact]
        public void Build_WikiOrderPreservedEvenIfInputIsShuffled()
        {
            // Input in reverse Wikipedia order; Build should re-sort by WikiEpisode.Order
            var episodes = new List<MatchedEpisode>
            {
                Matched(3, "Three"),
                Matched(1, "One"),
                Matched(2, "Two")
            };

            var result = _builder.Build(episodes);

            Assert.Equal("One", result[0].WikiTitle);
            Assert.Equal("Two", result[1].WikiTitle);
            Assert.Equal("Three", result[2].WikiTitle);
        }

        [Fact]
        public void Build_SingleEpisode_ReturnsOneEntry()
        {
            var episodes = new List<MatchedEpisode> { Matched(1, "Only Episode") };
            var result = _builder.Build(episodes);
            Assert.Single(result);
            Assert.Equal("Only Episode", result[0].WikiTitle);
        }

        [Fact]
        public void BuildPlayQueue_EmptyInput_ReturnsEmpty()
        {
            var result = _builder.BuildPlayQueue(new List<MatchedEpisode>());
            Assert.Empty(result);
        }
    }
}
