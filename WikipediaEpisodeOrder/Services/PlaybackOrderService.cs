using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Services
{
    public class PlaybackOrderService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly CacheService _cacheService;
        private readonly EpisodeMatcher _matcher;
        private readonly OrderBuilderService _orderBuilder;
        private readonly ILogger<PlaybackOrderService> _logger;

        public PlaybackOrderService(
            ILibraryManager libraryManager,
            CacheService cacheService,
            EpisodeMatcher matcher,
            OrderBuilderService orderBuilder,
            ILogger<PlaybackOrderService> logger)
        {
            _libraryManager = libraryManager;
            _cacheService = cacheService;
            _matcher = matcher;
            _orderBuilder = orderBuilder;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves all episodes (including specials from Season 0) for a series from the Jellyfin library.
        /// </summary>
        public IReadOnlyList<EpisodeMatcher.JellyfinCandidate> GetJellyfinEpisodes(Guid seriesId)
        {
            _logger.LogDebug("Querying Jellyfin library for series {SeriesId}", seriesId);

            var query = new InternalItemsQuery
            {
                ParentId = seriesId,
                Recursive = true,
                IncludeItemTypes = new[] { BaseItemKind.Episode }
            };

            var items = _libraryManager.GetItemList(query);

            var candidates = items
                .OfType<Episode>()
                .Select(e => new EpisodeMatcher.JellyfinCandidate(
                    e.Id,
                    e.Name ?? string.Empty,
                    e.PremiereDate))
                .ToList();

            _logger.LogInformation(
                "Found {Count} episodes in Jellyfin library for series {SeriesId}",
                candidates.Count, seriesId);

            return candidates;
        }

        /// <summary>
        /// Generates the full playback order for a series using cached Wikipedia data and Jellyfin library.
        /// </summary>
        public async Task<PlaybackResult> GetPlaybackOrderAsync(
            Guid seriesId,
            CancellationToken cancellationToken = default)
        {
            var cached = await _cacheService.ReadAsync(seriesId, cancellationToken).ConfigureAwait(false);
            if (cached == null)
            {
                _logger.LogWarning("No cached Wikipedia order for series {SeriesId}", seriesId);
                return new PlaybackResult(seriesId, Array.Empty<PlaybackEntry>(), 0, 0, null);
            }

            var jellyfinEpisodes = GetJellyfinEpisodes(seriesId);
            var matched = _matcher.Match(cached.Episodes, jellyfinEpisodes);
            var entries = _orderBuilder.Build(matched);

            int matchedCount = entries.Count(e => e.Matched);
            int unmatchedCount = entries.Count - matchedCount;

            _logger.LogInformation(
                "Playback order for {SeriesId}: {Matched} matched, {Unmatched} unmatched",
                seriesId, matchedCount, unmatchedCount);

            return new PlaybackResult(seriesId, entries, matchedCount, unmatchedCount, cached.LastUpdatedUtc);
        }

        /// <summary>
        /// Returns Jellyfin item IDs in Wikipedia playback order (unmatched episodes are skipped).
        /// </summary>
        public async Task<IReadOnlyList<Guid>> GetPlayQueueAsync(
            Guid seriesId,
            CancellationToken cancellationToken = default)
        {
            var cached = await _cacheService.ReadAsync(seriesId, cancellationToken).ConfigureAwait(false);
            if (cached == null) return Array.Empty<Guid>();

            var jellyfinEpisodes = GetJellyfinEpisodes(seriesId);
            var matched = _matcher.Match(cached.Episodes, jellyfinEpisodes);

            var queue = _orderBuilder.BuildPlayQueue(matched);
            _logger.LogInformation("Play queue for {SeriesId}: {Count} items", seriesId, queue.Count);
            return queue;
        }

        /// <summary>
        /// Returns status information for a series.
        /// </summary>
        public async Task<SeriesStatus> GetStatusAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            var cached = await _cacheService.ReadAsync(seriesId, cancellationToken).ConfigureAwait(false);
            var exists = _cacheService.Exists(seriesId);

            if (cached == null)
            {
                return new SeriesStatus(seriesId, exists, 0, 0, null, null);
            }

            var jellyfinEpisodes = GetJellyfinEpisodes(seriesId);
            var matched = _matcher.Match(cached.Episodes, jellyfinEpisodes);

            int matchedCount = matched.Count(m => m.Matched);
            return new SeriesStatus(
                seriesId,
                exists,
                matchedCount,
                matched.Count - matchedCount,
                cached.LastUpdatedUtc,
                cached.LastUpdatedUtc);
        }
    }

    public record PlaybackResult(
        Guid SeriesId,
        IReadOnlyList<PlaybackEntry> Entries,
        int MatchedCount,
        int UnmatchedCount,
        DateTime? LastRefreshUtc);

    public record SeriesStatus(
        Guid SeriesId,
        bool CacheExists,
        int MatchedCount,
        int UnmatchedCount,
        DateTime? CacheDate,
        DateTime? LastRefreshUtc);
}
