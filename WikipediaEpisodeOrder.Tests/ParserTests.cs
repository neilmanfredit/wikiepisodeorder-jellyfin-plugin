using System;
using System.Linq;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Tests
{
    public class ParserTests
    {
        private readonly WikipediaParser _parser;

        public ParserTests()
        {
            _parser = new WikipediaParser(NullLogger.Instance);
        }

        // ─── Helper ───────────────────────────────────────────────────────────────

        private static string WrapPage(string title, string body) =>
            $"<!DOCTYPE html><html><head><title>{title}</title></head><body>{body}</body></html>";

        private static string SeasonTable(int season, params (string epNum, string title, string airDate)[] eps)
        {
            var rows = string.Concat(eps.Select(e =>
                $"<tr><td>{e.epNum}</td><td>{e.title}</td><td>{e.airDate}</td></tr>"));

            return $@"
<h2>Season {season}</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Title</th><th>Original air date</th></tr>
  {rows}
</table>";
        }

        private static string SpecialsTable(string sectionTitle, params string[] titles)
        {
            var rows = string.Concat(titles.Select((t, i) =>
                $"<tr><td>{i + 1}</td><td>{t}</td><td></td></tr>"));

            return $@"
<h2>{sectionTitle}</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Title</th><th>Original air date</th></tr>
  {rows}
</table>";
        }

        // ─── Tests: series name extraction ────────────────────────────────────────

        [Fact]
        public void Parse_ExtractsSeriesNameFromTitle()
        {
            var html = WrapPage(
                "List of Firefly episodes - Wikipedia",
                SeasonTable(1, ("1", "Serenity", "December 20, 2002")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal("Firefly", result.SeriesName);
        }

        [Fact]
        public void Parse_FallsBackToH1ForSeriesName()
        {
            var html = WrapPage(
                "",
                "<h1>List of Red Dwarf episodes</h1>" +
                SeasonTable(1, ("1", "The End", "February 15, 1988")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal("Red Dwarf", result.SeriesName);
        }

        // ─── Tests: multiple seasons ──────────────────────────────────────────────

        [Fact]
        public void Parse_MultipleSeasonsPreservesDocumentOrder()
        {
            var html = WrapPage(
                "List of Futurama episodes - Wikipedia",
                SeasonTable(1,
                    ("1", "Space Pilot 3000", "March 28, 1999"),
                    ("2", "The Series Has Landed", "April 4, 1999")) +
                SeasonTable(2,
                    ("1", "A Flight to Remember", "September 26, 1999"),
                    ("2", "Mars University", "October 3, 1999")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal(4, result.Episodes.Count);
            Assert.Equal(1, result.Episodes[0].Order);
            Assert.Equal("Space Pilot 3000", result.Episodes[0].Title);
            Assert.Equal(4, result.Episodes[3].Order);
            Assert.Equal("Mars University", result.Episodes[3].Title);
        }

        [Fact]
        public void Parse_SeasonNumberAssignedCorrectly()
        {
            var html = WrapPage(
                "List of Only Fools and Horses episodes - Wikipedia",
                @"<h2>Series 1</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Series</th><th>Title</th><th>Original air date</th></tr>
  <tr><td>1</td><td>1</td><td>Big Brother</td><td>8 September 1981</td></tr>
  <tr><td>2</td><td>1</td><td>Go West Young Man</td><td>15 September 1981</td></tr>
</table>");

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal(2, result.Episodes.Count);
            Assert.Equal(1, result.Episodes[0].Season);
            Assert.Equal(1, result.Episodes[1].Season);
        }

        // ─── Tests: special detection ─────────────────────────────────────────────

        [Fact]
        public void Parse_SpecialsSectionMarksEpisodesAsSpecial()
        {
            var html = WrapPage(
                "List of Only Fools and Horses episodes - Wikipedia",
                SeasonTable(1,
                    ("1", "Big Brother", "8 September 1981"),
                    ("2", "Go West Young Man", "15 September 1981")) +
                SpecialsTable("Christmas Specials",
                    "Christmas Crackers",
                    "Only Fools and Horses (Christmas special)"));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal(4, result.Episodes.Count);
            Assert.False(result.Episodes[0].IsSpecial);
            Assert.False(result.Episodes[1].IsSpecial);
            Assert.True(result.Episodes[2].IsSpecial);
            Assert.True(result.Episodes[3].IsSpecial);
        }

        [Fact]
        public void Parse_TVMoviesMarkedAsSpecial()
        {
            var html = WrapPage(
                "List of Futurama episodes - Wikipedia",
                SeasonTable(1, ("1", "Space Pilot 3000", "March 28, 1999")) +
                SpecialsTable("TV Movies",
                    "Bender's Big Score",
                    "The Beast with a Billion Backs"));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            var movies = result.Episodes.Where(e => e.IsSpecial).ToList();
            Assert.Equal(2, movies.Count);
            Assert.Contains(movies, m => m.Title == "Bender's Big Score");
        }

        [Fact]
        public void Parse_SpecialsKeywordVariantsAreDetected()
        {
            foreach (var label in new[] { "Specials", "Holiday Specials", "TV Films", "Movies", "Reunion Episodes" })
            {
                var html = WrapPage(
                    "List of Test episodes - Wikipedia",
                    SpecialsTable(label, "Special Episode 1"));

                var result = _parser.Parse(html, "https://en.wikipedia.org/test");
                Assert.True(result.Episodes.All(e => e.IsSpecial),
                    $"Section '{label}' should mark episodes as special");
            }
        }

        // ─── Tests: air date parsing ──────────────────────────────────────────────

        [Fact]
        public void Parse_AirDateUSFormat()
        {
            var html = WrapPage(
                "List of Test episodes - Wikipedia",
                SeasonTable(1, ("1", "Pilot", "March 28, 1999")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Single(result.Episodes);
            Assert.NotNull(result.Episodes[0].AirDate);
            Assert.Equal(new DateTime(1999, 3, 28), result.Episodes[0].AirDate!.Value);
        }

        [Fact]
        public void Parse_AirDateUKFormat()
        {
            var html = WrapPage(
                "List of Test episodes - Wikipedia",
                SeasonTable(1, ("1", "The End", "15 February 1988")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Single(result.Episodes);
            Assert.NotNull(result.Episodes[0].AirDate);
            Assert.Equal(new DateTime(1988, 2, 15), result.Episodes[0].AirDate!.Value);
        }

        [Fact]
        public void Parse_HiddenDateSpanFormat()
        {
            var html = WrapPage(
                "List of Test episodes - Wikipedia",
                @"<h2>Season 1</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Title</th><th>Original air date</th></tr>
  <tr><td>1</td><td>Pilot</td><td><span>(2005-03-27)</span>27 March 2005</td></tr>
</table>");

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.NotNull(result.Episodes[0].AirDate);
            Assert.Equal(new DateTime(2005, 3, 27), result.Episodes[0].AirDate!.Value);
        }

        [Fact]
        public void Parse_MissingAirDateIsNull()
        {
            var html = WrapPage(
                "List of Test episodes - Wikipedia",
                SeasonTable(1, ("1", "Pilot", "")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Single(result.Episodes);
            Assert.Null(result.Episodes[0].AirDate);
        }

        // ─── Tests: ordering integrity ────────────────────────────────────────────

        [Fact]
        public void Parse_OrderIsSequentialAcrossAllTables()
        {
            var html = WrapPage(
                "List of Test episodes - Wikipedia",
                SeasonTable(1, ("1", "Alpha", "January 1, 2000"), ("2", "Beta", "January 8, 2000")) +
                SpecialsTable("Specials", "Gamma") +
                SeasonTable(2, ("3", "Delta", "January 15, 2000")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal(4, result.Episodes.Count);
            Assert.Equal(1, result.Episodes[0].Order);
            Assert.Equal(2, result.Episodes[1].Order);
            Assert.Equal(3, result.Episodes[2].Order); // special
            Assert.Equal(4, result.Episodes[3].Order);
            Assert.Equal("Gamma", result.Episodes[2].Title);
            Assert.True(result.Episodes[2].IsSpecial);
        }

        [Fact]
        public void Parse_NoWikitablesReturnsEmptyList()
        {
            var html = WrapPage("Test - Wikipedia", "<p>No tables here.</p>");
            var result = _parser.Parse(html, "https://en.wikipedia.org/test");
            Assert.Empty(result.Episodes);
        }

        // ─── Tests: title cleaning ────────────────────────────────────────────────

        [Fact]
        public void Parse_CitationMarkersRemovedFromTitle()
        {
            var html = WrapPage(
                "List of Test episodes - Wikipedia",
                @"<h2>Season 1</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Title</th><th>Original air date</th></tr>
  <tr><td>1</td><td>Pilot[1]</td><td></td></tr>
</table>");

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal("Pilot", result.Episodes[0].Title);
        }

        [Fact]
        public void Parse_QuotedTitleIsUnquoted()
        {
            var html = WrapPage(
                "List of Test episodes - Wikipedia",
                @"<h2>Season 1</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Title</th><th>Original air date</th></tr>
  <tr><td>1</td><td>&quot;Pilot&quot;</td><td></td></tr>
</table>");

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal("Pilot", result.Episodes[0].Title);
        }

        // ─── Tests: rowspan handling ──────────────────────────────────────────────

        [Fact]
        public void Parse_RowspanCellsFillCorrectly()
        {
            // Episode number spans 2 rows (two-part episode)
            var html = WrapPage(
                "List of Doctor Who episodes - Wikipedia",
                @"<h2>Series 1</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Title</th><th>Original air date</th></tr>
  <tr><td rowspan=""2"">1</td><td>Rose</td><td>26 March 2005</td></tr>
  <tr><td>The End of the World</td><td>2 April 2005</td></tr>
</table>");

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal(2, result.Episodes.Count);
            Assert.Equal("Rose", result.Episodes[0].Title);
            Assert.Equal("The End of the World", result.Episodes[1].Title);
            // Both get episode number 1 from the rowspan
            Assert.Equal(1, result.Episodes[0].EpisodeNumber);
            Assert.Equal(1, result.Episodes[1].EpisodeNumber);
        }

        // ─── Tests: metadata ──────────────────────────────────────────────────────

        [Fact]
        public void Parse_LastUpdatedUtcIsSet()
        {
            var before = DateTime.UtcNow;
            var html = WrapPage("List of Test episodes - Wikipedia",
                SeasonTable(1, ("1", "Pilot", "January 1, 2000")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.True(result.LastUpdatedUtc >= before);
        }

        [Fact]
        public void Parse_WikipediaUrlPreserved()
        {
            const string url = "https://en.wikipedia.org/wiki/List_of_Firefly_episodes";
            var html = WrapPage("List of Firefly episodes - Wikipedia",
                SeasonTable(1, ("1", "Serenity", "December 20, 2002")));

            var result = _parser.Parse(html, url);

            Assert.Equal(url, result.WikipediaUrl);
        }

        // ─── Tests: production code ───────────────────────────────────────────────

        // ─── Tests: wikiepisodetable format (real Wikipedia format) ──────────────────

        // Real Wikipedia episode tables use <th scope="row"> for the first data cell
        // (episode number) and class="wikiepisodetable". Section-marker rows and
        // expand-child description rows must be skipped.

        private static string WikiEpisodeTable(string sectionHeader, params (string epNum, string title, string airDate)[] eps)
        {
            var header = @"<tr>
      <th scope=""col""><abbr title=""Number"">No.</abbr></th>
      <th scope=""col"">Title</th>
      <th scope=""col"">Original release date</th>
    </tr>";

            var markerLabel = sectionHeader.Replace(" ", "&#95;");
            var sectionMarker = $@"<tr><td colspan=""5"" id=""{markerLabel}""><b>{sectionHeader}</b></td></tr>";

            var rows = string.Concat(eps.Select((e, i) => $@"
    <tr class=""vevent module-episode-list-row"">
      <th scope=""row"" rowspan=""1"" id=""ep{i + 1}"">{e.epNum}</th>
      <td class=""summary"">""{ e.title }""</td>
      <td>{e.airDate}</td>
    </tr>
    <tr class=""expand-child""><td colspan=""5"">Description of {e.title}</td></tr>"));

            return $@"
<h2>{sectionHeader}</h2>
<table class=""wikitable plainrowheaders wikiepisodetable"">
  <tbody>
    {header}
    {sectionMarker}
    {rows}
  </tbody>
</table>";
        }

        [Fact]
        public void Parse_WikiEpisodeTableExtractsEpisodesFromThScopeRow()
        {
            var html = WrapPage(
                "List of Only Fools and Horses episodes - Wikipedia",
                WikiEpisodeTable("Series One (1981)",
                    ("1", "Big Brother", "8 September 1981"),
                    ("2", "Go West Young Man", "15 September 1981")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal(2, result.Episodes.Count);
            Assert.Equal("Big Brother", result.Episodes[0].Title);
            Assert.Equal(1, result.Episodes[0].EpisodeNumber);
            Assert.Equal("Go West Young Man", result.Episodes[1].Title);
            Assert.Equal(2, result.Episodes[1].EpisodeNumber);
        }

        [Fact]
        public void Parse_WikiEpisodeTableSectionMarkersNotExtractedAsEpisodes()
        {
            var html = WrapPage(
                "List of Only Fools and Horses episodes - Wikipedia",
                WikiEpisodeTable("Series One (1981)",
                    ("1", "Big Brother", "8 September 1981")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Single(result.Episodes);
            Assert.Equal("Big Brother", result.Episodes[0].Title);
        }

        [Fact]
        public void Parse_WikiEpisodeTableExpandChildRowsNotExtractedAsEpisodes()
        {
            var html = WrapPage(
                "List of Only Fools and Horses episodes - Wikipedia",
                WikiEpisodeTable("Series One (1981)",
                    ("1", "Big Brother", "8 September 1981")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            // Should be exactly 1 episode, not 2 (the expand-child description row must be skipped)
            Assert.Single(result.Episodes);
        }

        [Fact]
        public void Parse_WikiEpisodeTableTitleInThScopeRow()
        {
            // Some OFAH tables omit the No. column and put the title directly in <th scope="row">
            var html = WrapPage(
                "List of Only Fools and Horses episodes - Wikipedia",
                @"<h2>Specials</h2>
<table class=""wikitable plainrowheaders wikiepisodetable"">
  <tbody>
    <tr>
      <th scope=""col"">Title</th>
      <th scope=""col"">Original release date</th>
    </tr>
    <tr class=""vevent module-episode-list-row"">
      <th class=""summary"" scope=""row"" rowspan=""1"">&quot;Christmas Trees&quot;</th>
      <td>27 December 1982</td>
    </tr>
    <tr class=""expand-child""><td colspan=""3"">Description</td></tr>
  </tbody>
</table>");

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Single(result.Episodes);
            Assert.Equal("Christmas Trees", result.Episodes[0].Title);
            Assert.Equal(new DateTime(1982, 12, 27), result.Episodes[0].AirDate!.Value);
        }

        [Fact]
        public void Parse_SeriesOverviewTableIsSkipped()
        {
            // Series overview tables have no "Title" column — they should be ignored entirely
            var html = WrapPage(
                "List of Only Fools and Horses episodes - Wikipedia",
                @"<table class=""wikitable"">
  <tbody>
    <tr>
      <th scope=""col"">Series</th>
      <th scope=""col"">Episodes</th>
      <th scope=""col"">Originally released</th>
    </tr>
    <tr>
      <th scope=""rowgroup"" rowspan=""2"">1</th>
      <td colspan=""2"">6</td>
      <td>8 September 1981</td>
    </tr>
  </tbody>
</table>" +
                WikiEpisodeTable("Series One",
                    ("1", "Big Brother", "8 September 1981")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Single(result.Episodes);
            Assert.Equal("Big Brother", result.Episodes[0].Title);
        }

        [Fact]
        public void Parse_MultipleSeasonsWithWikiEpisodeTableFormat()
        {
            var html = WrapPage(
                "List of Only Fools and Horses episodes - Wikipedia",
                WikiEpisodeTable("Series One",
                    ("1", "Big Brother", "8 September 1981"),
                    ("2", "Go West Young Man", "15 September 1981")) +
                WikiEpisodeTable("Series Two",
                    ("7", "The Long Legs of the Law", "21 October 1981"),
                    ("8", "Ashes to Ashes", "28 October 1981")));

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Equal(4, result.Episodes.Count);
            Assert.Equal(1, result.Episodes[0].Order);
            Assert.Equal(4, result.Episodes[3].Order);
            Assert.Equal("Ashes to Ashes", result.Episodes[3].Title);
        }

        [Fact]
        public void Parse_ProductionCodeExtractedWhenPresent()
        {
            var html = WrapPage(
                "List of Family Guy episodes - Wikipedia",
                @"<h2>Season 1</h2>
<table class=""wikitable"">
  <tr><th>No.</th><th>Title</th><th>Prod. code</th><th>Original air date</th></tr>
  <tr><td>1</td><td>Death Has a Shadow</td><td>1ACX01</td><td>January 31, 1999</td></tr>
</table>");

            var result = _parser.Parse(html, "https://en.wikipedia.org/test");

            Assert.Single(result.Episodes);
            Assert.Equal("1ACX01", result.Episodes[0].ProductionCode);
        }
    }
}
