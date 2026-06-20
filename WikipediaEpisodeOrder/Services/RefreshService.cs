using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Services
{
    public class RefreshService
    {
        private const int MaxRetries = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

        private readonly WikipediaEpisodeProvider _provider;
        private readonly CacheService _cache;
        private readonly ILogger<RefreshService> _logger;

        public RefreshService(
            WikipediaEpisodeProvider provider,
            CacheService cache,
            ILogger<RefreshService> logger)
        {
            _provider = provider;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Refreshes a single series: downloads, parses, and updates the cache.
        /// </summary>
        public async Task RefreshSeriesAsync(SeriesMapping mapping, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Starting refresh for '{SeriesName}' ({SeriesId}) from {Url}",
                mapping.SeriesName, mapping.SeriesId, mapping.WikipediaUrl);

            WikiSeriesOrder? order = null;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    order = await _provider.GetEpisodeOrderAsync(mapping.WikipediaUrl, cancellationToken)
                        .ConfigureAwait(false);

                    order.SeriesName = mapping.SeriesName; // use configured name as canonical
                    break;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Refresh cancelled for '{SeriesName}'", mapping.SeriesName);
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex,
                        "Attempt {Attempt}/{Max} failed for '{SeriesName}'",
                        attempt, MaxRetries, mapping.SeriesName);

                    if (attempt < MaxRetries)
                    {
                        await Task.Delay(RetryDelay * attempt, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Non-retryable error refreshing '{SeriesName}'", mapping.SeriesName);
                    throw;
                }
            }

            if (order == null)
            {
                _logger.LogError(lastException,
                    "All {Max} attempts failed for '{SeriesName}'", MaxRetries, mapping.SeriesName);
                return;
            }

            await _cache.WriteAsync(mapping.SeriesId, order, cancellationToken).ConfigureAwait(false);
            mapping.LastUpdatedUtc = order.LastUpdatedUtc;

            _logger.LogInformation(
                "Refresh complete for '{SeriesName}': {Count} episodes cached",
                mapping.SeriesName, order.Episodes.Count);
        }

        /// <summary>
        /// Checks all mappings and refreshes those that are expired or have no cache.
        /// </summary>
        public async Task RefreshAllAsync(
            IEnumerable<SeriesMapping> mappings,
            CancellationToken cancellationToken = default)
        {
            foreach (var mapping in mappings)
            {
                if (cancellationToken.IsCancellationRequested) break;

                bool needsRefresh = false;

                if (!_cache.Exists(mapping.SeriesId))
                {
                    _logger.LogDebug("No cache for '{SeriesName}', will refresh", mapping.SeriesName);
                    needsRefresh = true;
                }
                else if (mapping.AutoRefresh)
                {
                    var cached = await _cache.ReadAsync(mapping.SeriesId, cancellationToken)
                        .ConfigureAwait(false);
                    if (cached != null && _cache.IsExpired(cached, mapping.RefreshDays))
                    {
                        _logger.LogDebug(
                            "Cache expired for '{SeriesName}' (age > {Days} days), will refresh",
                            mapping.SeriesName, mapping.RefreshDays);
                        needsRefresh = true;
                    }
                }

                if (needsRefresh)
                {
                    await RefreshSeriesAsync(mapping, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("'{SeriesName}' cache is fresh, skipping", mapping.SeriesName);
                }
            }
        }
    }
}
