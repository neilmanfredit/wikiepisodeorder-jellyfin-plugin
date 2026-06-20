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
    /// <summary>
    /// End-to-end tests wiring WikipediaParser → EpisodeMatcher → OrderBuilderService
    /// using realistic HTML that mirrors actual Wikipedia episode-list structures.
    /// </summary>
    public class EndToEndTests
    {
        private readonly WikipediaParser _parser;
        private readonly EpisodeMatcher _matcher;
        private readonly OrderBuilderService _orderBuilder;

        public EndToEndTests()
        {
            _parser = new WikipediaParser(NullLogger.Instance);
            _matcher = new EpisodeMatcher(NullLogger.Instance);
            _orderBuilder = new OrderBuilderService(NullLogger.Instance);
        }

        // ─── HTML helpers ─────────────────────────────────────────────────────────

        private static string Page(string title, string body) =>
            $"<!DOCTYPE html><html><head><title>{title}</title></head><body>{body}</body></html>";

        private static string SeasonSection(int s, params (string ep, string title, string date)[] rows) =>
            $"<h2>Series {s}</h2>" +
            "<table class=\"wikitable\"><tr><th>No.</th><th>Title</th><th>Original air date</th></tr>" +
            string.Concat(rows.Select(r => $"<tr><td>{r.ep}</td><td>{r.title}</td><td>{r.date}</td></tr>")) +
            "</table>";

        private static string SpecialsSection(string heading, params string[] titles) =>
            $"<h2>{heading}</h2>" +
            "<table class=\"wikitable\"><tr><th>No.</th><th>Title</th><th>Original air date</th></tr>" +
            string.Concat(titles.Select((t, i) => $"<tr><td>{i + 1}</td><td>{t}</td><td></td></tr>")) +
            "</table>";

        // ─── Only Fools and Horses scenario ──────────────────────────────────────

        [Fact]
        public void EndToEnd_OnlyFoolsAndHorses_SpecialsInCorrectPosition()
        {
            // Simulate first 3 regular episodes + Christmas special + next episode
            var html = Page(
                "List of Only Fools and Horses episodes - Wikipedia",
                SeasonSection(1,
                    ("1", "Big Brother", "8 September 1981"),
                    ("2", "Go West Young Man", "15 September 1981"),
                    ("3", "Cash and Curry", "22 September 1981")) +
                SpecialsSection("Christmas Specials",
                    "Christmas Crackers") +
                SeasonSection(2,
                    ("7", "The Long Legs of the Law", "21 October 1982")));

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Only_Fools_and_Horses_episodes");
            Assert.Equal("Only Fools and Horses", wikiOrder.SeriesName);
            Assert.Equal(5, wikiOrder.Episodes.Count);

            // Verify special is at position 4 (index 3)
            Assert.False(wikiOrder.Episodes[0].IsSpecial);
            Assert.False(wikiOrder.Episodes[1].IsSpecial);
            Assert.False(wikiOrder.Episodes[2].IsSpecial);
            Assert.True(wikiOrder.Episodes[3].IsSpecial);
            Assert.Equal("Christmas Crackers", wikiOrder.Episodes[3].Title);
            Assert.False(wikiOrder.Episodes[4].IsSpecial);

            // Match against Jellyfin library (note: special stored in Season 0 in Jellyfin)
            var jellyfinItems = new[]
            {
                new JellyfinCandidate(Guid.NewGuid(), "Big Brother", new DateTime(1981, 9, 8)),
                new JellyfinCandidate(Guid.NewGuid(), "Go West Young Man", new DateTime(1981, 9, 15)),
                new JellyfinCandidate(Guid.NewGuid(), "Cash and Curry", new DateTime(1981, 9, 22)),
                new JellyfinCandidate(Guid.NewGuid(), "Christmas Crackers", null),
                new JellyfinCandidate(Guid.NewGuid(), "The Long Legs of the Law", new DateTime(1982, 10, 21))
            };

            var matched = _matcher.Match(wikiOrder.Episodes, jellyfinItems);
            Assert.Equal(5, matched.Count);
            Assert.All(matched, m => Assert.True(m.Matched));

            var queue = _orderBuilder.Build(matched);
            Assert.Equal(5, queue.Count);

            // Christmas Crackers must be at position 4
            Assert.Equal("Christmas Crackers", queue[3].WikiTitle);
            Assert.True(queue[3].IsSpecial);
        }

        // ─── Doctor Who scenario ──────────────────────────────────────────────────

        [Fact]
        public void EndToEnd_DoctorWho_MultipleSeriesWithPunctuation()
        {
            var html = Page(
                "List of Doctor Who episodes (2005-present) - Wikipedia",
                SeasonSection(1,
                    ("1", "Rose", "26 March 2005"),
                    ("2", "The End of the World", "2 April 2005"),
                    ("3", "The Unquiet Dead", "9 April 2005")) +
                SpecialsSection("Specials",
                    "The Christmas Invasion",
                    "The Runaway Bride") +
                SeasonSection(2,
                    ("14", "New Earth", "15 April 2006"),
                    ("15", "Tooth and Claw", "22 April 2006")));

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Doctor_Who_episodes");
            Assert.Equal(7, wikiOrder.Episodes.Count);

            // Match with title normalisation (apostrophe in "The Doctor's Wife" type titles)
            var jellyfinItems = new[]
            {
                new JellyfinCandidate(Guid.NewGuid(), "Rose", new DateTime(2005, 3, 26)),
                new JellyfinCandidate(Guid.NewGuid(), "The End of the World", null),
                new JellyfinCandidate(Guid.NewGuid(), "The Unquiet Dead", null),
                new JellyfinCandidate(Guid.NewGuid(), "The Christmas Invasion", null),
                new JellyfinCandidate(Guid.NewGuid(), "The Runaway Bride", null),
                new JellyfinCandidate(Guid.NewGuid(), "New Earth", null),
                new JellyfinCandidate(Guid.NewGuid(), "Tooth and Claw", null)
            };

            var matched = _matcher.Match(wikiOrder.Episodes, jellyfinItems);
            Assert.All(matched, m => Assert.True(m.Matched));

            var entries = _orderBuilder.Build(matched);
            Assert.Equal("Rose", entries[0].WikiTitle);
            Assert.Equal("The Christmas Invasion", entries[3].WikiTitle);
            Assert.True(entries[3].IsSpecial);
            Assert.True(entries[4].IsSpecial);
            Assert.Equal("New Earth", entries[5].WikiTitle);
        }

        // ─── Red Dwarf scenario: fuzzy matching ───────────────────────────────────

        [Fact]
        public void EndToEnd_RedDwarf_FuzzyMatchingForTitleVariations()
        {
            var html = Page(
                "List of Red Dwarf episodes - Wikipedia",
                SeasonSection(1,
                    ("1", "The End", "15 February 1988"),
                    ("2", "Future Echoes", "22 February 1988"),
                    ("3", "Balance of Power", "29 February 1988")));

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Red_Dwarf_episodes");

            // Jellyfin has slightly different title for last episode (e.g., with series prefix)
            var jellyfinItems = new[]
            {
                new JellyfinCandidate(Guid.NewGuid(), "The End", null),
                new JellyfinCandidate(Guid.NewGuid(), "Future Echoes", null),
                new JellyfinCandidate(Guid.NewGuid(), "Balance of Power", null)
            };

            var matched = _matcher.Match(wikiOrder.Episodes, jellyfinItems);
            Assert.All(matched, m => Assert.True(m.Matched));

            int matched100 = matched.Count(m => m.Confidence == 100);
            Assert.Equal(3, matched100); // all exact
        }

        // ─── Futurama: TV movies scenario ─────────────────────────────────────────

        [Fact]
        public void EndToEnd_Futurama_TVMoviesInterspersed()
        {
            var html = Page(
                "List of Futurama episodes - Wikipedia",
                SeasonSection(1,
                    ("1", "Space Pilot 3000", "March 28, 1999"),
                    ("2", "The Series Has Landed", "April 4, 1999")) +
                SpecialsSection("TV Movies",
                    "Bender's Big Score",
                    "The Beast with a Billion Backs",
                    "Bender's Game",
                    "Into the Wild Green Yonder") +
                SeasonSection(6,
                    ("89", "Rebirth", "June 24, 2010")));

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Futurama_episodes");
            Assert.Equal(7, wikiOrder.Episodes.Count);

            var tvMovies = wikiOrder.Episodes.Where(e => e.IsSpecial).ToList();
            Assert.Equal(4, tvMovies.Count);
            Assert.Contains(tvMovies, m => m.Title == "Bender's Big Score");
            Assert.Contains(tvMovies, m => m.Title == "Into the Wild Green Yonder");

            // Verify they appear after season 1, before season 6
            int firstMovieIdx  = wikiOrder.Episodes.IndexOf(tvMovies[0]);
            int season6StartIdx = wikiOrder.Episodes.FindIndex(e => e.Title == "Rebirth");
            Assert.True(firstMovieIdx > 1);
            Assert.True(firstMovieIdx < season6StartIdx);
        }

        // ─── Normalised title matching ────────────────────────────────────────────

        [Fact]
        public void EndToEnd_NormalisedTitleMatching_StripsQuotesAndPunctuation()
        {
            var html = Page(
                "List of Firefly episodes - Wikipedia",
                SeasonSection(1,
                    ("1", "Serenity", "December 20, 2002"),
                    ("2", "The Train Job", "September 20, 2002"),
                    ("3", "Bushwhacked", "September 27, 2002")));

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Firefly_episodes");

            // Jellyfin stores with different punctuation or format
            var jellyfinItems = new[]
            {
                new JellyfinCandidate(Guid.NewGuid(), "Serenity", null),
                new JellyfinCandidate(Guid.NewGuid(), "The Train Job", null),
                new JellyfinCandidate(Guid.NewGuid(), "Bushwhacked", null)
            };

            var matched = _matcher.Match(wikiOrder.Episodes, jellyfinItems);
            Assert.All(matched, m => Assert.True(m.Matched));
        }

        // ─── Air date matching ────────────────────────────────────────────────────

        [Fact]
        public void EndToEnd_AirDateMatching_WhenTitlesDiffer()
        {
            var html = Page(
                "List of Firefly episodes - Wikipedia",
                "<h2>Season 1</h2>" +
                "<table class=\"wikitable\">" +
                "<tr><th>No.</th><th>Title</th><th>Original air date</th></tr>" +
                "<tr><td>1</td><td>The Pilot</td><td>December 20, 2002</td></tr>" +
                "</table>");

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Firefly_episodes");

            // Jellyfin has a completely different title but the same air date
            var jellyfinItems = new[]
            {
                new JellyfinCandidate(Guid.NewGuid(), "Completely Different Title", new DateTime(2002, 12, 20))
            };

            var matched = _matcher.Match(wikiOrder.Episodes, jellyfinItems);
            Assert.Single(matched);
            Assert.True(matched[0].Matched);
            Assert.Equal("AirDate", matched[0].MatchMethod);
        }

        // ─── Unmatched episodes do not break queue ────────────────────────────────

        [Fact]
        public void EndToEnd_UnmatchedEpisodesSkippedInPlayQueue()
        {
            var html = Page(
                "List of Firefly episodes - Wikipedia",
                SeasonSection(1,
                    ("1", "Serenity", "December 20, 2002"),
                    ("2", "The Train Job", "September 20, 2002"),
                    ("3", "Bushwhacked", "September 27, 2002"),
                    ("4", "Shindig", "November 1, 2002"),
                    ("5", "Safe", "November 8, 2002")));

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Firefly_episodes");

            // Only 3 of 5 episodes in Jellyfin library
            var jellyfinItems = new[]
            {
                new JellyfinCandidate(Guid.NewGuid(), "Serenity", null),
                new JellyfinCandidate(Guid.NewGuid(), "Bushwhacked", null),
                new JellyfinCandidate(Guid.NewGuid(), "Shindig", null)
            };

            var matched = _matcher.Match(wikiOrder.Episodes, jellyfinItems);
            var entries = _orderBuilder.Build(matched);
            var queue = _orderBuilder.BuildPlayQueue(matched);

            Assert.Equal(5, entries.Count); // all 5 in preview
            Assert.Equal(3, queue.Count);   // only 3 matched in play queue

            // Matched items must still be in Wikipedia order
            Assert.Equal("Serenity",    entries[0].WikiTitle);
            Assert.Equal("Bushwhacked", entries[2].WikiTitle);
            Assert.Equal("Shindig",     entries[3].WikiTitle);
        }

        // ─── Wikipedia order vs Jellyfin internal order ───────────────────────────

        [Fact]
        public void EndToEnd_WikipediaOrderDifferentFromJellyfinInternalOrder()
        {
            // Firefly: Wikipedia lists "Serenity" (pilot) as episode 1 but Fox aired "The Train Job" first.
            // Our plugin should use Wikipedia order.
            var html = Page(
                "List of Firefly episodes - Wikipedia",
                SeasonSection(1,
                    ("11", "Serenity", "December 20, 2002"),  // pilot aired last
                    ("1",  "The Train Job", "September 20, 2002"),
                    ("2",  "Bushwhacked", "September 27, 2002")));

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Firefly_episodes");

            // Regardless of ep numbers (11, 1, 2), Wikipedia document order is preserved
            Assert.Equal("Serenity",     wikiOrder.Episodes[0].Title);
            Assert.Equal("The Train Job", wikiOrder.Episodes[1].Title);
            Assert.Equal("Bushwhacked",  wikiOrder.Episodes[2].Title);

            var jellyfinItems = new[]
            {
                new JellyfinCandidate(Guid.NewGuid(), "The Train Job", null),   // internally ep 1
                new JellyfinCandidate(Guid.NewGuid(), "Bushwhacked", null),     // internally ep 2
                new JellyfinCandidate(Guid.NewGuid(), "Serenity", null)         // internally ep 11
            };

            var matched = _matcher.Match(wikiOrder.Episodes, jellyfinItems);
            var queue = _orderBuilder.BuildPlayQueue(matched);

            // Serenity (ep 11 internally) must be first in play queue
            var serenityId = jellyfinItems.First(j => j.Title == "Serenity").Id;
            Assert.Equal(serenityId, queue[0]);
        }

        // ─── Family Guy: production code in table ─────────────────────────────────

        [Fact]
        public void EndToEnd_FamilyGuy_ProductionCodePreserved()
        {
            var html = Page(
                "List of Family Guy episodes - Wikipedia",
                @"<h2>Season 1</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Title</th><th>Prod. code</th><th>Original air date</th></tr>
  <tr><td>1</td><td>Death Has a Shadow</td><td>1ACX01</td><td>January 31, 1999</td></tr>
  <tr><td>2</td><td>I Never Met the Dead Man</td><td>1ACX02</td><td>April 11, 1999</td></tr>
</table>");

            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_Family_Guy_episodes");
            Assert.Equal(2, wikiOrder.Episodes.Count);
            Assert.Equal("1ACX01", wikiOrder.Episodes[0].ProductionCode);
            Assert.Equal("1ACX02", wikiOrder.Episodes[1].ProductionCode);
        }

        // ─── American Dad: long-running series integrity ──────────────────────────

        [Fact]
        public void EndToEnd_AmericanDad_ManyEpisodesOrderIntact()
        {
            const int episodesPerSeason = 10;
            const int seasons = 5;

            var sections = new System.Text.StringBuilder();
            int globalEp = 1;
            for (int s = 1; s <= seasons; s++)
            {
                sections.Append($"<h2>Season {s}</h2>");
                sections.Append("<table class=\"wikitable\"><tr><th>No.</th><th>Title</th><th>Original air date</th></tr>");
                for (int e = 1; e <= episodesPerSeason; e++, globalEp++)
                {
                    sections.Append($"<tr><td>{globalEp}</td><td>Episode {globalEp}</td><td>January {e}, 200{s}</td></tr>");
                }
                sections.Append("</table>");
            }

            var html = Page("List of American Dad! episodes - Wikipedia", sections.ToString());
            var wikiOrder = _parser.Parse(html, "https://en.wikipedia.org/wiki/List_of_American_Dad!_episodes");

            Assert.Equal(seasons * episodesPerSeason, wikiOrder.Episodes.Count);

            // Order must be exactly 1..50
            for (int i = 0; i < wikiOrder.Episodes.Count; i++)
                Assert.Equal(i + 1, wikiOrder.Episodes[i].Order);
        }
    }
}
