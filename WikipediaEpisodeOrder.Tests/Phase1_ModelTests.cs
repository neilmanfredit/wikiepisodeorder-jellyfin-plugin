using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Xunit;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Tests
{
    public class Phase1_ModelTests
    {
        [Fact]
        public void WikiEpisode_DefaultValues_AreCorrect()
        {
            var ep = new WikiEpisode();
            Assert.Equal(0, ep.Order);
            Assert.Equal(string.Empty, ep.Title);
            Assert.Null(ep.Season);
            Assert.Null(ep.EpisodeNumber);
            Assert.Null(ep.AirDate);
            Assert.Null(ep.ProductionCode);
            Assert.False(ep.IsSpecial);
            Assert.Null(ep.SourceSection);
        }

        [Fact]
        public void WikiEpisode_CanBeMarkedSpecial()
        {
            var ep = new WikiEpisode { Title = "Christmas Special", IsSpecial = true, Season = null };
            Assert.True(ep.IsSpecial);
            Assert.Null(ep.Season);
        }

        [Fact]
        public void WikiSeriesOrder_DefaultValues_AreCorrect()
        {
            var order = new WikiSeriesOrder();
            Assert.Equal(string.Empty, order.SeriesName);
            Assert.Equal(string.Empty, order.WikipediaUrl);
            Assert.NotNull(order.Episodes);
            Assert.Empty(order.Episodes);
        }

        [Fact]
        public void WikiSeriesOrder_CanHoldEpisodes()
        {
            var order = new WikiSeriesOrder
            {
                SeriesName = "Only Fools and Horses",
                WikipediaUrl = "https://en.wikipedia.org/wiki/List_of_Only_Fools_and_Horses_episodes",
                LastUpdatedUtc = DateTime.UtcNow,
                Episodes = new List<WikiEpisode>
                {
                    new WikiEpisode { Order = 1, Title = "Big Brother", Season = 1, EpisodeNumber = 1 },
                    new WikiEpisode { Order = 2, Title = "Go West Young Man", Season = 1, EpisodeNumber = 2 },
                    new WikiEpisode { Order = 7, Title = "Christmas Crackers", IsSpecial = true }
                }
            };

            Assert.Equal(3, order.Episodes.Count);
            Assert.True(order.Episodes[2].IsSpecial);
        }

        [Fact]
        public void MatchedEpisode_DefaultValues_AreCorrect()
        {
            var match = new MatchedEpisode();
            Assert.NotNull(match.WikiEpisode);
            Assert.Equal(Guid.Empty, match.JellyfinItemId);
            Assert.Equal(string.Empty, match.JellyfinTitle);
            Assert.Equal(0, match.Confidence);
            Assert.Equal(string.Empty, match.MatchMethod);
            Assert.False(match.Matched);
        }

        [Fact]
        public void MatchedEpisode_CanBePopulated()
        {
            var id = Guid.NewGuid();
            var match = new MatchedEpisode
            {
                WikiEpisode = new WikiEpisode { Order = 1, Title = "Pilot" },
                JellyfinItemId = id,
                JellyfinTitle = "Pilot",
                Confidence = 100,
                MatchMethod = "ExactTitle",
                Matched = true
            };

            Assert.Equal(id, match.JellyfinItemId);
            Assert.Equal(100, match.Confidence);
            Assert.True(match.Matched);
            Assert.Equal("ExactTitle", match.MatchMethod);
        }

        [Fact]
        public void SeriesMapping_DefaultValues_AreCorrect()
        {
            var mapping = new SeriesMapping();
            Assert.Equal(Guid.Empty, mapping.SeriesId);
            Assert.Equal(string.Empty, mapping.SeriesName);
            Assert.Equal(string.Empty, mapping.WikipediaUrl);
            Assert.False(mapping.AutoRefresh);
            Assert.Equal(7, mapping.RefreshDays);
        }

        [Fact]
        public void SeriesMapping_CanBeConfigured()
        {
            var id = Guid.NewGuid();
            var mapping = new SeriesMapping
            {
                SeriesId = id,
                SeriesName = "Doctor Who",
                WikipediaUrl = "https://en.wikipedia.org/wiki/List_of_Doctor_Who_episodes_(2005%E2%80%93present)",
                AutoRefresh = true,
                RefreshDays = 14,
                LastUpdatedUtc = DateTime.UtcNow
            };

            Assert.Equal(id, mapping.SeriesId);
            Assert.Equal("Doctor Who", mapping.SeriesName);
            Assert.True(mapping.AutoRefresh);
            Assert.Equal(14, mapping.RefreshDays);
        }

        [Fact]
        public void PluginConfiguration_DefaultContainsEmptyMappingList()
        {
            var config = new PluginConfiguration();
            Assert.NotNull(config.Mappings);
            Assert.Empty(config.Mappings);
        }

        [Fact]
        public void PluginConfiguration_CanHoldMultipleMappings()
        {
            var config = new PluginConfiguration();
            config.Mappings.Add(new SeriesMapping { SeriesName = "Firefly" });
            config.Mappings.Add(new SeriesMapping { SeriesName = "Futurama" });
            Assert.Equal(2, config.Mappings.Count);
        }
    }
}
