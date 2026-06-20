using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Services
{
    public class CacheService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _cacheDirectory;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public CacheService(IApplicationPaths applicationPaths, ILogger logger)
        {
            _logger = logger;
            _cacheDirectory = Path.Combine(
                applicationPaths.PluginConfigurationsPath,
                "WikipediaEpisodeOrder",
                "cache");

            Directory.CreateDirectory(_cacheDirectory);
        }

        // Ctor for testing (explicit cache directory)
        public CacheService(string cacheDirectory, ILogger logger)
        {
            _logger = logger;
            _cacheDirectory = cacheDirectory;
            Directory.CreateDirectory(_cacheDirectory);
        }

        private string CacheFilePath(Guid seriesId) =>
            Path.Combine(_cacheDirectory, $"{seriesId}.json");

        public async Task<WikiSeriesOrder?> ReadAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            var path = CacheFilePath(seriesId);
            if (!File.Exists(path))
            {
                _logger.LogDebug("Cache miss for series {SeriesId}", seriesId);
                return null;
            }

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var stream = File.OpenRead(path);
                var cached = await JsonSerializer.DeserializeAsync<WikiSeriesOrder>(
                    stream, JsonOptions, cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("Cache hit for series {SeriesId}, last updated {Date}",
                    seriesId, cached?.LastUpdatedUtc);
                return cached;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read cache for series {SeriesId}", seriesId);
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task WriteAsync(Guid seriesId, WikiSeriesOrder order, CancellationToken cancellationToken = default)
        {
            var path = CacheFilePath(seriesId);

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var stream = File.Create(path);
                await JsonSerializer.SerializeAsync(stream, order, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Cached {Count} episodes for series {SeriesId}",
                    order.Episodes.Count, seriesId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write cache for series {SeriesId}", seriesId);
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Delete(Guid seriesId)
        {
            var path = CacheFilePath(seriesId);
            if (!File.Exists(path)) return;

            try
            {
                File.Delete(path);
                _logger.LogInformation("Deleted cache for series {SeriesId}", seriesId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete cache for series {SeriesId}", seriesId);
            }
        }

        public bool IsExpired(WikiSeriesOrder cached, int refreshDays)
        {
            if (refreshDays <= 0) return false;
            var age = DateTime.UtcNow - cached.LastUpdatedUtc;
            return age.TotalDays >= refreshDays;
        }

        public bool Exists(Guid seriesId) => File.Exists(CacheFilePath(seriesId));
    }
}
