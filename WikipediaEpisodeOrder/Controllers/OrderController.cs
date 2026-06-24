using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Controllers
{
    [ApiController]
    [Route("WikipediaOrder")]
    [Produces(MediaTypeNames.Application.Json)]
    public class OrderController : ControllerBase
    {
        private readonly PlaybackOrderService _playbackOrderService;
        private readonly RefreshService _refreshService;
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IAuthorizationContext _authorizationContext;
        private readonly ILogger<OrderController> _logger;

        public OrderController(
            PlaybackOrderService playbackOrderService,
            RefreshService refreshService,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IAuthorizationContext authorizationContext,
            ILogger<OrderController> logger)
        {
            _playbackOrderService = playbackOrderService;
            _refreshService = refreshService;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _authorizationContext = authorizationContext;
            _logger = logger;
        }

        /// <summary>
        /// Returns the full Wikipedia-ordered episode list for a series, with match status for each entry.
        /// </summary>
        [HttpGet("{seriesId}/preview")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PreviewResponse>> GetPreview(
            [FromRoute] string seriesId,
            CancellationToken cancellationToken)
        {
            var result = await _playbackOrderService.GetPlaybackOrderAsync(Guid.Parse(seriesId), cancellationToken)
                .ConfigureAwait(false);

            if (result.Entries.Count == 0)
                return NotFound(new { message = $"No Wikipedia order found for series {seriesId}. Refresh first." });

            return Ok(new PreviewResponse
            {
                SeriesId = Guid.Parse(seriesId),
                LastRefreshUtc = result.LastRefreshUtc,
                MatchedCount = result.MatchedCount,
                UnmatchedCount = result.UnmatchedCount,
                Entries = result.Entries.Select(e => new PreviewEntry
                {
                    Position = e.Position,
                    WikiOrder = e.WikiOrder,
                    WikiTitle = e.WikiTitle,
                    IsSpecial = e.IsSpecial,
                    Matched = e.Matched,
                    JellyfinItemId = e.JellyfinItemId,
                    JellyfinTitle = e.JellyfinTitle,
                    Confidence = e.Confidence,
                    MatchMethod = e.MatchMethod
                }).ToList()
            });
        }

        /// <summary>
        /// Triggers a download and cache refresh for a specific series.
        /// </summary>
        [HttpPost("{seriesId}/refresh")]
        [Authorize(Policy = "RequiresElevation")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> Refresh(
            [FromRoute] string seriesId,
            CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            var mapping = config?.Mappings.FirstOrDefault(m => string.Equals(m.SeriesId, seriesId, StringComparison.OrdinalIgnoreCase));

            if (mapping == null)
                return NotFound(new { message = $"Series {seriesId} is not configured." });

            try
            {
                await _refreshService.RefreshSeriesAsync(mapping, cancellationToken).ConfigureAwait(false);
                Plugin.Instance?.SaveConfiguration();
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh failed for series {SeriesId}", seriesId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Refresh failed. Check server logs for details." });
            }
        }

        /// <summary>
        /// Rebuilds episode-to-Jellyfin-item mappings for a series without re-downloading Wikipedia.
        /// </summary>
        [HttpPost("{seriesId}/rebuild")]
        [Authorize(Policy = "RequiresElevation")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Rebuild(
            [FromRoute] string seriesId,
            CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config?.Mappings.All(m => !string.Equals(m.SeriesId, seriesId, StringComparison.OrdinalIgnoreCase)) != false)
                return NotFound(new { message = $"Series {seriesId} is not configured." });

            // Rebuild just re-runs match + order building (cache already has Wikipedia data).
            var result = await _playbackOrderService.GetPlaybackOrderAsync(Guid.Parse(seriesId), cancellationToken)
                .ConfigureAwait(false);

            if (result.Entries.Count == 0)
                return NotFound(new { message = "No cached Wikipedia data. Run refresh first." });

            return NoContent();
        }

        /// <summary>
        /// Returns match/unmatch counts, cache date, and last refresh timestamp for a series.
        /// </summary>
        [HttpGet("{seriesId}/status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<StatusResponse>> GetStatus(
            [FromRoute] string seriesId,
            CancellationToken cancellationToken)
        {
            var status = await _playbackOrderService.GetStatusAsync(Guid.Parse(seriesId), cancellationToken)
                .ConfigureAwait(false);

            return Ok(new StatusResponse
            {
                SeriesId = Guid.Parse(seriesId),
                CacheExists = status.CacheExists,
                MatchedCount = status.MatchedCount,
                UnmatchedCount = status.UnmatchedCount,
                CacheDate = status.CacheDate,
                LastRefreshUtc = status.LastRefreshUtc
            });
        }

        /// <summary>
        /// Returns an ordered list of Jellyfin item IDs for a series in Wikipedia playback order.
        /// Unmatched episodes are excluded.
        /// </summary>
        [HttpGet("{seriesId}/playqueue")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PlayQueueResponse>> GetPlayQueue(
            [FromRoute] string seriesId,
            CancellationToken cancellationToken)
        {
            var queue = await _playbackOrderService.GetPlayQueueAsync(Guid.Parse(seriesId), cancellationToken)
                .ConfigureAwait(false);

            if (queue.Count == 0)
                return NotFound(new { message = $"No play queue available for series {seriesId}." });

            return Ok(new PlayQueueResponse
            {
                SeriesId = Guid.Parse(seriesId),
                ItemIds = queue.ToList()
            });
        }

        /// <summary>
        /// Creates (or replaces) a Jellyfin playlist for a series in Wikipedia episode order.
        /// </summary>
        [HttpPost("{seriesId}/playlist")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PlaylistResult>> CreatePlaylist(
            [FromRoute] string seriesId,
            CancellationToken cancellationToken)
        {
            var result = await _playbackOrderService.GetPlaybackOrderAsync(Guid.Parse(seriesId), cancellationToken)
                .ConfigureAwait(false);

            if (result.Entries.Count == 0)
                return NotFound(new { message = $"No Wikipedia order found for series {seriesId}. Refresh first." });

            var matchedIds = result.Entries
                .Where(e => e.Matched && e.JellyfinItemId.HasValue)
                .OrderBy(e => e.Position)
                .Select(e => e.JellyfinItemId!.Value)
                .ToList();

            if (matchedIds.Count == 0)
                return BadRequest(new { message = "No matched episodes to add to a playlist." });

            var seriesItem = _libraryManager.GetItemById(Guid.Parse(seriesId));
            var playlistName = $"{seriesItem?.Name ?? seriesId} — Wikipedia Order";

            var authInfo = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
            var userId = authInfo.UserId;

            var existing = _playlistManager.GetPlaylists(userId)
                .Where(p => string.Equals(p.Name, playlistName, StringComparison.Ordinal) && p.OwnerUserId == userId)
                .ToList();
            foreach (var pl in existing)
                _libraryManager.DeleteItem(pl, new DeleteOptions { DeleteFileLocation = true });

            var created = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = playlistName,
                ItemIdList = matchedIds,
                UserId = userId
            }).ConfigureAwait(false);

            return Ok(new PlaylistResult
            {
                PlaylistId = created.Id,
                Name = playlistName,
                ItemCount = matchedIds.Count
            });
        }

        /// <summary>
        /// Searches the Jellyfin library for TV series matching the query string.
        /// </summary>
        [HttpGet("series/search")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<SeriesSearchResult>> SearchSeries([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Ok(Array.Empty<SeriesSearchResult>());

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                SearchTerm = q,
                Limit = 20
            };

            var results = _libraryManager.GetItemList(query);
            return Ok(results.Select(item => new SeriesSearchResult
            {
                Id = item.Id,
                Name = item.Name ?? string.Empty
            }));
        }
    }

    // ─── Response DTOs ────────────────────────────────────────────────────────────

    public class PreviewResponse
    {
        public Guid SeriesId { get; set; }
        public DateTime? LastRefreshUtc { get; set; }
        public int MatchedCount { get; set; }
        public int UnmatchedCount { get; set; }
        public List<PreviewEntry> Entries { get; set; } = new();
    }

    public class PreviewEntry
    {
        public int Position { get; set; }
        public int WikiOrder { get; set; }
        public string WikiTitle { get; set; } = string.Empty;
        public bool IsSpecial { get; set; }
        public bool Matched { get; set; }
        public Guid? JellyfinItemId { get; set; }
        public string? JellyfinTitle { get; set; }
        public double Confidence { get; set; }
        public string MatchMethod { get; set; } = string.Empty;
    }

    public class StatusResponse
    {
        public Guid SeriesId { get; set; }
        public bool CacheExists { get; set; }
        public int MatchedCount { get; set; }
        public int UnmatchedCount { get; set; }
        public DateTime? CacheDate { get; set; }
        public DateTime? LastRefreshUtc { get; set; }
    }

    public class PlayQueueResponse
    {
        public Guid SeriesId { get; set; }
        public List<Guid> ItemIds { get; set; } = new();
    }

    public class SeriesSearchResult
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class PlaylistResult
    {
        public string PlaylistId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int ItemCount { get; set; }
    }
}
