# WIKIPEDIA EPISODE ORDER PLUGIN
## MASTER IMPLEMENTATION PROMPT

---

## AGENT ROLE

You are a senior software architect, senior C# developer, Jellyfin plugin developer, QA engineer, and technical writer.

You are building a complete production-ready Jellyfin plugin.

You must work until the entire project is complete.

Do not leave TODOs.

Do not create stubs.

Do not create mock implementations.

Do not create placeholder methods.

Everything must compile.

Everything must be production quality.

If a build error occurs, fix it immediately.

If a test fails, fix it immediately.

Never stop on partial implementation.

---

# PROJECT NAME

Wikipedia Episode Order

---

# PROJECT GOAL

Create a Jellyfin plugin that allows users to use a Wikipedia episode-list page as the authoritative source for TV show playback ordering.

The plugin must correctly insert:

- Episodes
- Specials
- Christmas Episodes
- Holiday Episodes
- TV Movies
- Reunion Specials
- Broadcast Events
- Feature-Length Episodes

into the proper playback sequence.

The plugin must support any show that has a Wikipedia episode list page.

No show-specific logic may exist.

No hardcoded support for any television show may exist.

---

# USER STORY

A user opens a TV series in Jellyfin.

The user supplies:

Series:
Only Fools and Horses

and

https://en.wikipedia.org/wiki/List_of_Only_Fools_and_Horses_episodes

The plugin parses Wikipedia.

The plugin matches episodes to Jellyfin items.

The plugin creates a canonical broadcast order.

The plugin places specials in the correct locations.

The user clicks:

Play Wikipedia Order

Playback follows Wikipedia chronology.

---

# TECHNOLOGY STACK

Use:

- .NET 8
- C#
- Jellyfin Plugin Framework

Supported Jellyfin:

- Jellyfin 10.10+

---

# PACKAGES

Install:

- HtmlAgilityPack
- AngleSharp
- FuzzySharp
- Microsoft.Extensions.Caching.Memory

Install any additional packages required for:

- HTTP
- JSON
- Testing
- Dependency Injection

provided they are compatible with Jellyfin.

---

# ARCHITECTURE

Create the following structure.

WikipediaEpisodeOrder/

├── Plugin.cs  
├── PluginConfiguration.cs  

├── Models/  
│   ├── WikiEpisode.cs  
│   ├── WikiSeriesOrder.cs  
│   ├── MatchedEpisode.cs  
│   └── SeriesMapping.cs  

├── Interfaces/  
│   └── IEpisodeOrderProvider.cs  

├── Providers/  
│   └── WikipediaEpisodeProvider.cs  

├── Services/  
│   ├── WikipediaParser.cs  
│   ├── EpisodeMatcher.cs  
│   ├── CacheService.cs  
│   ├── RefreshService.cs  
│   ├── OrderBuilderService.cs  
│   └── PlaybackOrderService.cs  

├── Controllers/  
│   └── OrderController.cs  

├── ScheduledTasks/  
│   └── RefreshWikipediaTask.cs  

├── Web/  
│   ├── configPage.html  
│   ├── configPage.js  
│   ├── orderPreview.html  
│   └── orderPreview.js  

├── Tests/  
│   ├── ParserTests.cs  
│   ├── MatchingTests.cs  
│   ├── OrderingTests.cs  
│   └── CacheTests.cs  

└── README.md

---

# PLUGIN CONFIGURATION

Create:

```csharp
public class SeriesMapping
{
    public Guid SeriesId { get; set; }

    public string SeriesName { get; set; }

    public string WikipediaUrl { get; set; }

    public bool AutoRefresh { get; set; }

    public int RefreshDays { get; set; }

    public DateTime LastUpdatedUtc { get; set; }
}
```

Plugin configuration contains:

```csharp
List<SeriesMapping>
```

Support multiple configured shows.

---

# MODEL: WikiEpisode

```csharp
public class WikiEpisode
{
    public int Order { get; set; }

    public string Title { get; set; }

    public int? Season { get; set; }

    public int? EpisodeNumber { get; set; }

    public DateTime? AirDate { get; set; }

    public string ProductionCode { get; set; }

    public bool IsSpecial { get; set; }

    public string SourceSection { get; set; }
}
```

---

# MODEL: WikiSeriesOrder

```csharp
public class WikiSeriesOrder
{
    public string SeriesName { get; set; }

    public string WikipediaUrl { get; set; }

    public DateTime LastUpdatedUtc { get; set; }

    public List<WikiEpisode> Episodes { get; set; }
}
```

---

# MODEL: MatchedEpisode

```csharp
public class MatchedEpisode
{
    public WikiEpisode WikiEpisode { get; set; }

    public Guid JellyfinItemId { get; set; }

    public string JellyfinTitle { get; set; }

    public double Confidence { get; set; }

    public string MatchMethod { get; set; }

    public bool Matched { get; set; }
}
```

---

# PROVIDER SYSTEM

Create abstraction.

```csharp
public interface IEpisodeOrderProvider
{
    Task<WikiSeriesOrder> GetEpisodeOrderAsync(string source);
}
```

Implement:

- WikipediaEpisodeProvider

Design system so future providers can be added without changing consumer code.

Future providers:

- TMDB
- TVDB
- TVMaze
- JSON
- XML
- CSV

Only Wikipedia provider should be implemented.

---

# WIKIPEDIA PARSER

Create:

WikipediaParser

Responsibilities:

1. Download HTML page.
2. Parse DOM.
3. Discover episode tables.
4. Extract episode metadata.
5. Preserve original order.
6. Detect specials.
7. Return canonical episode list.

Parser must support:

- Multiple season tables
- Special episode tables
- Movie tables
- Event tables
- Long-running series
- Anime pages
- Sitcom episode pages

Do not hardcode selectors for any individual show.

Parser must tolerate:

- Missing columns
- Different layouts
- Nested tables
- Merged rows
- Merged cells
- Multiple wikitable structures

---

# SPECIAL DETECTION

Mark episodes as special when:

- Season number absent
- Table labels indicate specials
- Table labels indicate movies
- Table labels indicate events
- Numbering is non-standard

Examples:

- Specials
- Christmas Specials
- Holiday Specials
- TV Films
- Movies
- Reunion Episodes
- Broadcast Events

Set:

```csharp
IsSpecial = true;
```

---

# EPISODE MATCHING

Create:

EpisodeMatcher

Matching order:

## Level 1

Exact title

Confidence:

100

## Level 2

Normalized title

Remove:

- apostrophes
- punctuation
- quotes
- dashes
- periods
- commas

Confidence:

95

## Level 3

Air date matching

Confidence:

90

## Level 4

Fuzzy matching

Use:

```csharp
Fuzz.Ratio()
```

Threshold:

90

Store:

- Match method
- Confidence score

---

# ORDER BUILDER

Create:

OrderBuilderService

Input:

```csharp
List<MatchedEpisode>
```

Output:

Canonical playback sequence.

Rule:

Wikipedia order always wins.

Never reorder using:

- Season number
- Episode number
- Production code

The parser ordering becomes playback ordering.

Example:

Episode 1

Episode 2

Christmas Special

Episode 3

Movie

Episode 4

---

# CACHE SERVICE

Create:

CacheService

Storage location:

```text
/plugins/configurations/WikipediaEpisodeOrder/cache
```

Cache file:

```text
{SeriesId}.json
```

Store:

```json
{
  "LastUpdatedUtc":"",
  "Episodes":[]
}
```

Implement:

- Read
- Write
- Refresh
- Delete
- Validate expiration

---

# REFRESH SERVICE

Create:

RefreshService

Responsibilities:

- Check mappings
- Download latest wiki page
- Parse
- Update cache
- Update timestamps

Include:

- Retry logic
- Logging
- Exception handling

---

# SCHEDULED TASK

Create:

RefreshWikipediaTask

Runs:

Daily

Respects:

```csharp
RefreshDays
```

Only updates series requiring refresh.

---

# JELLYFIN LIBRARY INTEGRATION

Discover:

- Series
- Episodes
- Specials
- Season 0
- Attached movies

Expose items to EpisodeMatcher.

---

# PLAYBACK ORDER SERVICE

Create:

PlaybackOrderService

Input:

SeriesId

Output:

Ordered Jellyfin item identifiers.

Requirements:

- Preserve Wikipedia order
- Skip missing items
- Log unmatched episodes
- Generate playback queue

---

# API CONTROLLER

Create:

OrderController

Endpoints:

### Preview

```http
GET /WikipediaOrder/{seriesId}/preview
```

Returns ordered sequence.

### Refresh

```http
POST /WikipediaOrder/{seriesId}/refresh
```

Refreshes source.

### Rebuild

```http
POST /WikipediaOrder/{seriesId}/rebuild
```

Rebuilds mappings.

### Status

```http
GET /WikipediaOrder/{seriesId}/status
```

Returns:

- Match count
- Unmatched count
- Cache date
- Last refresh

### Play Queue

```http
GET /WikipediaOrder/{seriesId}/playqueue
```

Returns ordered Jellyfin IDs.

---

# ADMIN UI

Create:

- configPage.html
- configPage.js

Capabilities:

- Add mapping
- Edit mapping
- Delete mapping
- Refresh now
- Preview order

Fields:

- Series selector
- Wikipedia URL
- Auto refresh
- Refresh interval

Persist all changes.

---

# ORDER PREVIEW UI

Create:

- orderPreview.html
- orderPreview.js

Display:

- Order
- Wikipedia title
- Matched episode
- Confidence
- Status

Colour coding:

- Green = matched
- Yellow = partial
- Red = unmatched

---

# LOGGING

Use Jellyfin logging infrastructure.

Log:

- Download start
- Download end
- Table detection
- Episode extraction
- Matching results
- Cache updates
- Refreshes
- Queue generation
- Errors

Include detailed diagnostics.

---

# TEST FRAMEWORK

Create unit testing project.

Minimum:

80 percent coverage.

---

# PARSER TESTS

Verify:

- Multiple seasons
- Movie tables
- Specials
- Air dates
- Ordering integrity

---

# MATCH TESTS

Verify:

- Exact title matching
- Normalized title matching
- Air date matching
- Fuzzy matching

---

# ORDER TESTS

Verify:

- Wikipedia order preserved
- Special insertion preserved
- No secondary sorting occurs

---

# CACHE TESTS

Verify:

- Write
- Read
- Refresh
- Stale cache detection

---

# REFERENCE VALIDATION DATA

Use the following pages during development tests:

- Only Fools and Horses
- Doctor Who
- Red Dwarf
- Futurama
- Firefly
- Family Guy
- American Dad
- Star Trek TNG

Do not hard-code support for these shows.

Use only for validation.

---

# GITHUB ACTIONS

Create CI pipeline.

Requirements:

- Restore
- Build
- Test
- Package plugin artifacts

Run on:

- Push
- Pull Request

---

# README

Generate complete documentation.

Include:

- Overview
- Features
- Architecture
- Installation
- Configuration
- Troubleshooting
- API Reference
- Development Notes
- Future Extensions

---

# DEVELOPMENT WORKFLOW

For every task:

1. Implement
2. Build
3. Test
4. Fix failures
5. Commit

Never continue with failing tests.

Never continue with build errors.

---

# COMPLETION CRITERIA

Project is complete only when:

- Build succeeds
- Tests pass
- API works
- Configuration UI works
- Parser works
- Refresh task works
- Cache works
- Playback queue generation works
- Documentation exists
- GitHub Actions passes

When finished provide:

PROJECT COMPLETE

Then include:

- Build status
- Test status
- File count
- Coverage estimate
- Remaining limitations
- Future enhancements

---

# IMPLEMENTATION EXECUTION PLAN

Execute development in this order:

Phase 1:
- Repository setup
- Plugin metadata
- Configuration
- Models

Phase 2:
- Provider abstraction
- Wikipedia provider
- Parser engine

Phase 3:
- Unit tests
- Parser validation

Phase 4:
- Episode matching engine

Phase 5:
- Order builder

Phase 6:
- Cache layer

Phase 7:
- Refresh service

Phase 8:
- Scheduled task

Phase 9:
- Jellyfin library integration

Phase 10:
- Playback queue generation

Phase 11:
- API controller

Phase 12:
- Admin UI

Phase 13:
- Preview UI

Phase 14:
- GitHub Actions

Phase 15:
- Documentation

Phase 16:
- End-to-end testing

Complete all phases before declaring success.
