using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Services
{
    public class WikipediaParser
    {
        private static readonly HashSet<string> SpecialSectionKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "special", "specials", "christmas special", "christmas specials",
            "holiday special", "holiday specials", "tv movie", "tv movies",
            "film", "films", "movie", "movies", "reunion", "event", "events",
            "miniseries", "mini-series", "one-off", "one off", "pilot",
            "webisode", "web special", "bonus episode", "bonus episodes",
            "oav", "ova", "feature", "feature length",
            "documentary", "documentaries"
        };

        // Date formats encountered on Wikipedia episode lists
        private static readonly string[] DateFormats =
        {
            "MMMM d, yyyy", "d MMMM yyyy", "yyyy-MM-dd",
            "MMM d, yyyy", "d MMM yyyy", "MMMM yyyy", "yyyy",
            "dd/MM/yyyy", "MM/dd/yyyy"
        };

        private readonly ILogger _logger;

        public WikipediaParser(ILogger logger)
        {
            _logger = logger;
        }

        public WikiSeriesOrder Parse(string html, string sourceUrl)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var seriesName = ExtractSeriesName(doc);
            _logger.LogDebug("Detected series name: {Name}", seriesName);

            var episodes = new List<WikiEpisode>();
            var order = 1;

            // Find all wikitable elements in document order
            var tables = doc.DocumentNode.SelectNodes("//table[contains(@class,'wikitable')]");
            if (tables == null || tables.Count == 0)
            {
                _logger.LogWarning("No wikitable elements found in {Url}", sourceUrl);
                return new WikiSeriesOrder
                {
                    SeriesName = seriesName,
                    WikipediaUrl = sourceUrl,
                    LastUpdatedUtc = DateTime.UtcNow,
                    Episodes = episodes
                };
            }

            _logger.LogDebug("Found {Count} wikitable(s)", tables.Count);

            foreach (var table in tables)
            {
                var sectionLabel = GetNearestSectionLabel(table);
                bool isSectionSpecial = IsSpecialSection(sectionLabel);

                _logger.LogDebug("Processing table with section label: '{Label}' (special={Special})", sectionLabel, isSectionSpecial);

                var columns = DetectColumns(table);
                if (columns.Count == 0)
                {
                    _logger.LogDebug("Skipping table - no recognizable columns");
                    continue;
                }

                if (columns.TitleIndex < 0)
                {
                    _logger.LogDebug("Skipping table - no title column detected");
                    continue;
                }

                var rows = table.SelectNodes(".//tr");
                if (rows == null) continue;

                // Track rowspan state: columnIndex -> remainingRows (filled value)
                var rowspanTracker = new Dictionary<int, (int remaining, string value)>();
                bool headerSkipped = false;

                foreach (var row in rows)
                {
                    // Skip header rows (th-only rows)
                    if (IsHeaderRow(row))
                    {
                        headerSkipped = true;
                        continue;
                    }

                    if (!headerSkipped) continue; // skip rows before first header

                    // Skip section-marker rows and expand-child description rows
                    if (ShouldSkipRow(row)) continue;

                    var cells = GetEffectiveCells(row, rowspanTracker);
                    var ep = ExtractEpisode(cells, columns, sectionLabel, isSectionSpecial, order);
                    if (ep != null)
                    {
                        episodes.Add(ep);
                        order++;
                    }
                }
            }

            _logger.LogInformation("Total episodes extracted: {Count}", episodes.Count);

            return new WikiSeriesOrder
            {
                SeriesName = seriesName,
                WikipediaUrl = sourceUrl,
                LastUpdatedUtc = DateTime.UtcNow,
                Episodes = episodes
            };
        }

        // ─── Section detection ────────────────────────────────────────────────────

        private string GetNearestSectionLabel(HtmlNode table)
        {
            // Walk backwards through siblings and parent nodes to find the nearest heading
            var current = table.PreviousSibling;
            while (current != null)
            {
                if (current.NodeType == HtmlNodeType.Element)
                {
                    var tag = current.Name.ToLowerInvariant();
                    if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                        return CleanText(current.InnerText);

                    // Also look inside divs for headings
                    var heading = current.SelectSingleNode(".//h2|.//h3|.//h4");
                    if (heading != null)
                        return CleanText(heading.InnerText);
                }
                current = current.PreviousSibling;
            }

            // Walk up to parent and try again
            var parent = table.ParentNode;
            while (parent != null && parent.Name is not "body" and not "#document")
            {
                var heading = parent.SelectSingleNode(
                    "preceding-sibling::h2[1]|preceding-sibling::h3[1]|preceding-sibling::h4[1]");
                if (heading != null)
                    return CleanText(heading.InnerText);
                parent = parent.ParentNode;
            }

            return string.Empty;
        }

        private static bool IsSpecialSection(string sectionLabel)
        {
            if (string.IsNullOrWhiteSpace(sectionLabel)) return false;

            var lower = sectionLabel.ToLowerInvariant();

            // Direct keyword match
            foreach (var kw in SpecialSectionKeywords)
            {
                if (lower.Contains(kw))
                    return true;
            }

            // Season X pattern — not special
            if (Regex.IsMatch(lower, @"\bseries\s*\d+\b|\bseason\s*\d+\b"))
                return false;

            return false;
        }

        // ─── Column detection ─────────────────────────────────────────────────────

        private record ColumnMap(int TitleIndex, int SeasonIndex, int EpNumIndex, int AirDateIndex, int ProdCodeIndex)
        {
            public int Count => new[] { TitleIndex, SeasonIndex, EpNumIndex, AirDateIndex, ProdCodeIndex }.Max() + 1;
        }

        private ColumnMap DetectColumns(HtmlNode table)
        {
            // Look for first row that contains <th> elements
            var headerRows = table.SelectNodes(".//tr[th]");
            if (headerRows == null || headerRows.Count == 0)
                return new ColumnMap(-1, -1, -1, -1, -1);

            int titleIdx = -1, seasonIdx = -1, epNumIdx = -1, airDateIdx = -1, prodCodeIdx = -1;

            foreach (var headerRow in headerRows)
            {
                var ths = headerRow.SelectNodes(".//th");
                if (ths == null) continue;

                for (int i = 0; i < ths.Count; i++)
                {
                    var text = CleanText(ths[i].InnerText).ToLowerInvariant();

                    if (titleIdx < 0 && (text.Contains("title") || text == "episode" || text.Contains("name")))
                        titleIdx = i;
                    else if (seasonIdx < 0 && (text.Contains("series") || text.Contains("season")))
                        seasonIdx = i;
                    else if (epNumIdx < 0 && (text == "no." || text.Contains("no.") || text == "#" || text.Contains("episode") || Regex.IsMatch(text, @"^no\.?\s*$")))
                        epNumIdx = i;
                    else if (airDateIdx < 0 && (text.Contains("air") || text.Contains("date") || text.Contains("broadcast") || text.Contains("premiere")))
                        airDateIdx = i;
                    else if (prodCodeIdx < 0 && (text.Contains("prod") || text.Contains("code")))
                        prodCodeIdx = i;
                }

                if (titleIdx >= 0) break; // found usable header row
            }

            return new ColumnMap(titleIdx, seasonIdx, epNumIdx, airDateIdx, prodCodeIdx);
        }

        // ─── Row processing ───────────────────────────────────────────────────────

        private static bool IsHeaderRow(HtmlNode row)
        {
            var cells = row.SelectNodes(".//th|.//td");
            if (cells == null) return true;
            var ths = row.SelectNodes(".//th");
            return ths != null && ths.Count == cells.Count;
        }

        private static bool ShouldSkipRow(HtmlNode row)
        {
            // expand-child rows contain episode descriptions, not episode data
            if (row.GetAttributeValue("class", "").Contains("expand-child"))
                return true;

            // Section-marker rows: single cell with large colspan (e.g. <td colspan="13">Series One (1981)</td>)
            var cells = row.ChildNodes
                .Where(n => n.NodeType == HtmlNodeType.Element && (n.Name == "td" || n.Name == "th"))
                .ToList();
            if (cells.Count == 1 && cells[0].GetAttributeValue("colspan", 1) > 3)
                return true;

            return false;
        }

        // Handle rowspan: returns the effective cell text list, advancing rowspan counters
        private static List<string> GetEffectiveCells(HtmlNode row, Dictionary<int, (int remaining, string value)> rowspanTracker)
        {
            // Include td elements and th elements that are row/rowgroup headers (scope != "col").
            // Wikipedia's wikiepisodetable format uses <th scope="row"> for the first data cell
            // (episode number or title), which must be included to keep column indices aligned.
            var tds = row.ChildNodes
                .Where(n => n.NodeType == HtmlNodeType.Element &&
                            (n.Name == "td" || (n.Name == "th" && n.GetAttributeValue("scope", "") != "col")))
                .ToList();
            if (tds.Count == 0 && rowspanTracker.Count == 0) return new List<string>();

            var result = new List<string>();
            int sourceIdx = 0;
            int colIdx = 0;

            while (sourceIdx < tds.Count || rowspanTracker.Count > 0)
            {
                if (rowspanTracker.TryGetValue(colIdx, out var span) && span.remaining > 0)
                {
                    result.Add(span.value);
                    rowspanTracker[colIdx] = (span.remaining - 1, span.value);
                    if (rowspanTracker[colIdx].remaining == 0)
                        rowspanTracker.Remove(colIdx);
                    colIdx++;
                    continue;
                }

                if (sourceIdx >= tds.Count) break;

                var td = tds[sourceIdx++];
                var text = CleanText(td.InnerText);

                int rowspan = 1;
                var rowspanAttr = td.GetAttributeValue("rowspan", "1");
                if (int.TryParse(rowspanAttr, out int rs) && rs > 1)
                {
                    rowspan = rs - 1; // remaining additional rows
                    rowspanTracker[colIdx] = (rowspan, text);
                }

                int colspan = td.GetAttributeValue("colspan", 1);
                for (int c = 0; c < colspan; c++)
                {
                    result.Add(text);
                    colIdx++;
                }
            }

            return result;
        }

        // ─── Episode extraction ───────────────────────────────────────────────────

        private WikiEpisode? ExtractEpisode(
            List<string> cells,
            ColumnMap columns,
            string sectionLabel,
            bool isSectionSpecial,
            int order)
        {
            if (cells.Count == 0) return null;

            string title = columns.TitleIndex >= 0 && columns.TitleIndex < cells.Count
                ? cells[columns.TitleIndex]
                : cells.FirstOrDefault(c => c.Length > 2) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Clean up Wikipedia title formatting artifacts
            title = CleanEpisodeTitle(title);
            if (string.IsNullOrWhiteSpace(title)) return null;

            int? season = null;
            if (columns.SeasonIndex >= 0 && columns.SeasonIndex < cells.Count)
                season = ParseInt(cells[columns.SeasonIndex]);

            // When there is no in-table season column, try to extract season number
            // from the section label (e.g. "Series 1 (1981)" or "Season 3").
            if (season == null && !string.IsNullOrWhiteSpace(sectionLabel))
            {
                var seasonMatch = Regex.Match(sectionLabel, @"\b(?:series|season)\s*(\d+)\b", RegexOptions.IgnoreCase);
                if (seasonMatch.Success)
                    season = int.Parse(seasonMatch.Groups[1].Value);
            }

            int? epNum = null;
            if (columns.EpNumIndex >= 0 && columns.EpNumIndex < cells.Count)
                epNum = ParseInt(cells[columns.EpNumIndex]);

            DateTime? airDate = null;
            if (columns.AirDateIndex >= 0 && columns.AirDateIndex < cells.Count)
                airDate = ParseDate(cells[columns.AirDateIndex]);

            string? prodCode = null;
            if (columns.ProdCodeIndex >= 0 && columns.ProdCodeIndex < cells.Count)
            {
                var code = cells[columns.ProdCodeIndex];
                if (!string.IsNullOrWhiteSpace(code))
                    prodCode = code;
            }

            // Mark as special if the section heading says so.
            // Only additionally mark as special when there IS an explicit season column
            // whose value is null — absence of a season column alone is not sufficient
            // (many regular-episode tables simply don't include a season column).
            bool isSpecial = isSectionSpecial
                || (columns.SeasonIndex >= 0 && season == null);

            return new WikiEpisode
            {
                Order = order,
                Title = title,
                Season = season,
                EpisodeNumber = epNum,
                AirDate = airDate,
                ProductionCode = prodCode,
                IsSpecial = isSpecial,
                SourceSection = sectionLabel
            };
        }

        private static bool HasSeasonFromOtherHints(List<string> cells, ColumnMap columns)
        {
            // If we see a numeric season-like value in any cell we haven't identified, treat as regular
            if (columns.SeasonIndex < 0) return false;
            return false; // season column exists but value was null/empty = likely special
        }

        // ─── Title cleaning ───────────────────────────────────────────────────────

        private static string CleanEpisodeTitle(string raw)
        {
            // Remove Wikipedia citation markers [1], [note 1], etc.
            var title = Regex.Replace(raw, @"\[\d+\]|\[note\s*\d+\]|\[citation needed\]", string.Empty, RegexOptions.IgnoreCase);

            // Remove parenthetical episode number e.g. "(episode 1)"
            title = Regex.Replace(title, @"\(episode\s*\d+\)", string.Empty, RegexOptions.IgnoreCase);

            // Normalize whitespace
            title = Regex.Replace(title, @"\s+", " ").Trim();

            // Remove surrounding quotes that sometimes appear
            if (title.StartsWith('"') && title.EndsWith('"') && title.Length > 2)
                title = title[1..^1].Trim();
            if (title.StartsWith('“') && title.EndsWith('”') && title.Length > 2)
                title = title[1..^1].Trim();

            return title;
        }

        // ─── Series name extraction ───────────────────────────────────────────────

        private static string ExtractSeriesName(HtmlDocument doc)
        {
            // Try <title> tag: "List of X episodes - Wikipedia"
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                var raw = CleanText(titleNode.InnerText);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var match = Regex.Match(raw, @"List of (.+?) episodes", RegexOptions.IgnoreCase);
                    if (match.Success)
                        return match.Groups[1].Value.Trim();

                    // Remove " - Wikipedia" suffix
                    raw = Regex.Replace(raw, @"\s*[-–|]\s*Wikipedia.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrWhiteSpace(raw))
                        return raw;
                }
            }

            // Fallback: h1
            var h1 = doc.DocumentNode.SelectSingleNode("//h1");
            if (h1 != null)
            {
                var raw = CleanText(h1.InnerText);
                var match = Regex.Match(raw, @"List of (.+?) episodes", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(raw))
                    return raw;
            }

            return string.Empty;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Decode common HTML entities and normalise
            text = text.Replace("&amp;", "&")
                       .Replace("&lt;", "<")
                       .Replace("&gt;", ">")
                       .Replace("&quot;", "\"")
                       .Replace("&nbsp;", " ")
                       .Replace(" ", " ");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static int? ParseInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            // Handle ranges like "1-2" or "1–2" — take first number
            var match = Regex.Match(text.Trim(), @"^\d+");
            if (match.Success && int.TryParse(match.Value, out int val))
                return val;
            return null;
        }

        private static DateTime? ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Strip Wikipedia hidden date spans like (2004-01-01)
            var match = Regex.Match(text, @"\((\d{4}-\d{2}-\d{2})\)");
            if (match.Success && DateTime.TryParseExact(
                    match.Groups[1].Value, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
                return d1;

            // Try all known formats
            text = Regex.Replace(text, @"\[.*?\]", string.Empty).Trim(); // strip citations
            foreach (var fmt in DateFormats)
            {
                if (DateTime.TryParseExact(text, fmt, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt))
                    return dt;
            }

            // Last resort: general parse
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt2))
                return dt2;

            return null;
        }
    }
}
