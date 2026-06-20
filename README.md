# Wikipedia Episode Order

A [Jellyfin](https://jellyfin.org) plugin that uses Wikipedia episode-list pages as the authoritative source for TV show playback ordering, correctly inserting specials, Christmas episodes, TV movies, reunion specials, and feature-length episodes into their proper chronological positions.

**Author:** Neil Manfred  
**Repository:** https://github.com/neilmanfredit/wikiepisodeorder-jellyfin-plugin  
**License:** CC BY-NC-ND 4.0 (non-commercial, no derivatives)  
**Minimum Jellyfin:** 10.10.0  
**Target Framework:** .NET 8.0

---

## Overview

Some TV series have complex broadcast histories where specials, one-off episodes, and TV movies were aired *between* regular seasons. Standard metadata providers (TMDB, TVDB) typically list specials in a separate "Season 0" bucket, breaking the intended viewing order.

Wikipedia episode-list pages encode the *actual broadcast order* as determined by fans and editors. This plugin parses those pages and generates a canonical playback sequence that matches the original broadcast chronology.

---

## Features

- **Wikipedia parsing** — downloads and parses any Wikipedia episode-list page, supporting tables across multiple seasons, specials, TV movies, reunions, and events
- **Broad compatibility** — works with any show that has a Wikipedia episode list; no show-specific code
- **4-level episode matching** — exact title, normalised title (strips punctuation), air date, and fuzzy matching (FuzzySharp) with configurable thresholds
- **Wikipedia order always wins** — no secondary sorting by season number, episode number, or production code
- **Automatic caching** — episode orders cached to JSON files; configurable per-series refresh interval
- **Scheduled daily refresh** — built-in Jellyfin scheduled task refreshes stale caches at 03:00
- **REST API** — preview, refresh, rebuild, status, and play-queue endpoints
- **Admin UI** — configuration page with add/edit/delete mappings, refresh, and preview
- **Order preview UI** — full episode table with colour-coded match status (green/orange/red), confidence scores, and special badges

---

## Architecture

```
WikipediaEpisodeOrder/
├── Plugin.cs                       # Plugin entry point, IHasWebPages
├── PluginConfiguration.cs          # Persisted config (List<SeriesMapping>)
│
├── Models/
│   ├── WikiEpisode.cs              # Single parsed Wikipedia episode
│   ├── WikiSeriesOrder.cs          # Full episode list for a series
│   ├── MatchedEpisode.cs           # Wiki episode + Jellyfin item link
│   └── SeriesMapping.cs            # User-configured series → URL mapping
│
├── Interfaces/
│   └── IEpisodeOrderProvider.cs    # Abstraction for future providers (TMDB, TVDB, etc.)
│
├── Providers/
│   └── WikipediaEpisodeProvider.cs # IEpisodeOrderProvider implementation for Wikipedia
│
├── Services/
│   ├── WikipediaParser.cs          # HTML parsing with HtmlAgilityPack
│   ├── EpisodeMatcher.cs           # 4-level cascade matching
│   ├── OrderBuilderService.cs      # Wikipedia-ordered playback entry list
│   ├── CacheService.cs             # JSON file cache per series
│   ├── RefreshService.cs           # Download + cache refresh with retry
│   └── PlaybackOrderService.cs     # Orchestrates library query + match + order
│
├── Controllers/
│   └── OrderController.cs          # REST API endpoints
│
├── ScheduledTasks/
│   └── RefreshWikipediaTask.cs     # Daily IScheduledTask
│
└── Web/
    ├── configPage.html / .js       # Admin configuration UI
    └── orderPreview.html / .js     # Episode order preview UI
```

---

## Installation

### From GitHub Releases (recommended)

1. Download the latest `WikipediaEpisodeOrder_x.x.x.x.zip` from [Releases](https://github.com/neilmanfredit/wikiepisodeorder-jellyfin-plugin/releases)
2. Extract the zip and copy the `plugin/` folder contents to your Jellyfin plugins directory:
   - Linux: `~/.local/share/jellyfin/plugins/WikipediaEpisodeOrder/`
   - Docker: `/config/plugins/WikipediaEpisodeOrder/`
3. Restart Jellyfin

### Manual Build

```bash
git clone https://github.com/neilmanfredit/wikiepisodeorder-jellyfin-plugin.git
cd wikiepisodeorder-jellyfin-plugin
dotnet build WikipediaEpisodeOrder.sln -c Release
# Plugin DLL at: WikipediaEpisodeOrder/bin/Release/net8.0/Jellyfin.Plugin.WikipediaEpisodeOrder.dll
```

---

## Configuration

1. In Jellyfin Admin Dashboard → **Plugins** → **Wikipedia Episode Order** → **Settings**
2. Click **Add Series Mapping**
3. Fill in:
   - **Jellyfin Series ID** — the GUID from the series URL (`/web/#/details?id=...`)
   - **Series Name** — display name (e.g. "Only Fools and Horses")
   - **Wikipedia Episode List URL** — full URL of the Wikipedia episode list page  
     e.g. `https://en.wikipedia.org/wiki/List_of_Only_Fools_and_Horses_episodes`
   - **Auto Refresh** — tick to enable scheduled cache refreshes
   - **Refresh Interval** — days between automatic refreshes (default: 7)
4. Click **Save** then **Save Configuration**
5. Click **Refresh Now** on the mapping to populate the cache immediately

---

## Usage

### Play Queue

After configuring and refreshing:

```http
GET /WikipediaOrder/{seriesId}/playqueue
```

Returns an ordered JSON array of Jellyfin item IDs. Feed these to Jellyfin's play API or a frontend playlist.

### Preview Order

Navigate to the **Preview** button on a mapping in the config page, or:

```http
GET /WikipediaOrder/{seriesId}/preview
```

Shows the full ordered table with match status for each episode.

---

## API Reference

All endpoints are under `/WikipediaOrder/{seriesId}/`.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/preview` | Full episode order with match/unmatch status |
| `POST` | `/refresh` | Re-download and cache from Wikipedia *(admin)* |
| `POST` | `/rebuild` | Re-match without re-downloading *(admin)* |
| `GET` | `/status` | Match counts, cache date, last refresh |
| `GET` | `/playqueue` | Ordered Jellyfin item IDs (unmatched skipped) |

### Preview response shape

```json
{
  "seriesId": "...",
  "lastRefreshUtc": "2025-01-01T03:00:00Z",
  "matchedCount": 65,
  "unmatchedCount": 3,
  "entries": [
    {
      "position": 1,
      "wikiOrder": 1,
      "wikiTitle": "Big Brother",
      "isSpecial": false,
      "matched": true,
      "jellyfinItemId": "...",
      "jellyfinTitle": "Big Brother",
      "confidence": 100,
      "matchMethod": "ExactTitle"
    }
  ]
}
```

### Status response shape

```json
{
  "seriesId": "...",
  "cacheExists": true,
  "matchedCount": 65,
  "unmatchedCount": 3,
  "cacheDate": "2025-01-01T03:00:00Z",
  "lastRefreshUtc": "2025-01-01T03:00:00Z"
}
```

---

## Episode Matching

Matching runs in cascading order; each level only processes episodes not yet matched:

| Level | Method | Confidence |
|-------|--------|------------|
| 1 | Exact title match | 100% |
| 2 | Normalised title (strips `'`,`.`,`,`,`-`,`"`) | 95% |
| 3 | Air date match | 90% |
| 4 | Fuzzy title (FuzzySharp `Fuzz.Ratio ≥ 90`) | Ratio value |

Each Jellyfin item is consumed by exactly one match.

---

## Special Detection

Episodes are flagged `IsSpecial = true` when:

- The section heading contains keywords: *specials, christmas specials, holiday specials, tv movies, films, reunion, events, pilot, oav/ova, miniseries…*
- The table has an explicit season column whose value is null for that row

Specials are placed in the playback queue at their **Wikipedia position** — not moved to Season 0.

---

## Cache Storage

Cache files are stored at:

```
{JellyfinDataDir}/plugins/configurations/WikipediaEpisodeOrder/cache/{SeriesId}.json
```

Each file contains the full `WikiSeriesOrder` JSON including episode list and `lastUpdatedUtc`.

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Preview shows 0 episodes | Cache not populated | Click **Refresh Now** |
| Many unmatched episodes | Title formatting differs | Check episode titles in Jellyfin vs Wikipedia; fuzzy match covers minor differences |
| Wrong order | Wikipedia page has unusual table layout | Check the parser logs; open a GitHub issue with the URL |
| Refresh fails | Network error or Wikipedia rate-limiting | Plugin retries 3 times with backoff; check server logs |
| Scheduled task not running | Jellyfin task scheduler issue | Trigger manually via Dashboard → Scheduled Tasks |

---

## Development Notes

### Building

```bash
dotnet build WikipediaEpisodeOrder.sln -c Release
```

### Testing

Tests target `net10.0` to run on the available SDK; plugin itself targets `net8.0` for Jellyfin 10.10.x compatibility.

```bash
dotnet test WikipediaEpisodeOrder.sln -c Release
```

### Adding a New Provider

1. Implement `IEpisodeOrderProvider` in a new class under `Providers/`
2. Register it in Jellyfin's DI container
3. No changes to consumer code required

---

## Future Enhancements

- TMDB, TVDB, TVMaze, JSON, XML, CSV providers via `IEpisodeOrderProvider`
- Jellyfin "Play All" integration with automatic queue injection
- UI series auto-complete by querying the Jellyfin library
- Wikipedia URL auto-discovery from series metadata
- Per-episode confidence threshold configuration
- Coverage reporting in CI via Coverlet + Report Generator
- Plugin repository manifest for one-click install from Jellyfin Dashboard

---

## License

Copyright © Neil Manfred. Licensed under the [Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International License](LICENSE). Commercial use and derivative works are not permitted.
