using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Tests
{
    public class CacheTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly CacheService _cache;

        public CacheTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WikiEpisodeOrderTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            _cache = new CacheService(_tempDir, NullLogger.Instance);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private static WikiSeriesOrder MakeOrder(string seriesName = "Test Series", int episodeCount = 3)
        {
            var episodes = new List<WikiEpisode>();
            for (int i = 1; i <= episodeCount; i++)
                episodes.Add(new WikiEpisode { Order = i, Title = $"Episode {i}", Season = 1, EpisodeNumber = i });

            return new WikiSeriesOrder
            {
                SeriesName = seriesName,
                WikipediaUrl = "https://en.wikipedia.org/test",
                LastUpdatedUtc = DateTime.UtcNow,
                Episodes = episodes
            };
        }

        // ─── Write ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Write_CreatesFileOnDisk()
        {
            var id = Guid.NewGuid();
            var order = MakeOrder();

            await _cache.WriteAsync(id, order);

            var path = Path.Combine(_tempDir, $"{id}.json");
            Assert.True(File.Exists(path));
        }

        [Fact]
        public async Task Write_FileContainsEpisodeData()
        {
            var id = Guid.NewGuid();
            var order = MakeOrder(episodeCount: 5);

            await _cache.WriteAsync(id, order);

            var path = Path.Combine(_tempDir, $"{id}.json");
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("Episode 1", content);
            Assert.Contains("Episode 5", content);
        }

        // ─── Read ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Read_RoundTripsAllData()
        {
            var id = Guid.NewGuid();
            var original = MakeOrder("Firefly", 3);
            original.Episodes[0].IsSpecial = true;
            original.Episodes[0].AirDate = new DateTime(2002, 12, 20);

            await _cache.WriteAsync(id, original);
            var read = await _cache.ReadAsync(id);

            Assert.NotNull(read);
            Assert.Equal("Firefly", read!.SeriesName);
            Assert.Equal(3, read.Episodes.Count);
            Assert.True(read.Episodes[0].IsSpecial);
            Assert.Equal(new DateTime(2002, 12, 20), read.Episodes[0].AirDate);
        }

        [Fact]
        public async Task Read_ReturnsNullWhenFileDoesNotExist()
        {
            var result = await _cache.ReadAsync(Guid.NewGuid());
            Assert.Null(result);
        }

        [Fact]
        public async Task Read_ReturnsNullForCorruptFile()
        {
            var id = Guid.NewGuid();
            var path = Path.Combine(_tempDir, $"{id}.json");
            await File.WriteAllTextAsync(path, "{ this is invalid json {{{{");

            var result = await _cache.ReadAsync(id);
            Assert.Null(result);
        }

        // ─── Delete ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Delete_RemovesFile()
        {
            var id = Guid.NewGuid();
            await _cache.WriteAsync(id, MakeOrder());

            _cache.Delete(id);

            Assert.False(_cache.Exists(id));
        }

        [Fact]
        public void Delete_DoesNotThrowWhenFileDoesNotExist()
        {
            // Should silently succeed
            _cache.Delete(Guid.NewGuid());
        }

        // ─── Exists ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Exists_TrueAfterWrite()
        {
            var id = Guid.NewGuid();
            await _cache.WriteAsync(id, MakeOrder());
            Assert.True(_cache.Exists(id));
        }

        [Fact]
        public void Exists_FalseWhenNotWritten()
        {
            Assert.False(_cache.Exists(Guid.NewGuid()));
        }

        [Fact]
        public async Task Exists_FalseAfterDelete()
        {
            var id = Guid.NewGuid();
            await _cache.WriteAsync(id, MakeOrder());
            _cache.Delete(id);
            Assert.False(_cache.Exists(id));
        }

        // ─── Expiration ───────────────────────────────────────────────────────────

        [Fact]
        public void IsExpired_FalseWhenFresh()
        {
            var order = new WikiSeriesOrder { LastUpdatedUtc = DateTime.UtcNow };
            Assert.False(_cache.IsExpired(order, refreshDays: 7));
        }

        [Fact]
        public void IsExpired_TrueWhenOld()
        {
            var order = new WikiSeriesOrder { LastUpdatedUtc = DateTime.UtcNow.AddDays(-8) };
            Assert.True(_cache.IsExpired(order, refreshDays: 7));
        }

        [Fact]
        public void IsExpired_FalseWhenRefreshDaysIsZero()
        {
            // RefreshDays = 0 means auto-refresh disabled
            var order = new WikiSeriesOrder { LastUpdatedUtc = DateTime.UtcNow.AddDays(-100) };
            Assert.False(_cache.IsExpired(order, refreshDays: 0));
        }

        [Fact]
        public void IsExpired_TrueExactlyAtBoundary()
        {
            var order = new WikiSeriesOrder { LastUpdatedUtc = DateTime.UtcNow.AddDays(-7) };
            Assert.True(_cache.IsExpired(order, refreshDays: 7));
        }

        // ─── Refresh (overwrite) ──────────────────────────────────────────────────

        [Fact]
        public async Task Write_OverwritesExistingCache()
        {
            var id = Guid.NewGuid();
            var first = MakeOrder(episodeCount: 2);
            var second = MakeOrder(episodeCount: 10);

            await _cache.WriteAsync(id, first);
            await _cache.WriteAsync(id, second);

            var read = await _cache.ReadAsync(id);
            Assert.NotNull(read);
            Assert.Equal(10, read!.Episodes.Count);
        }

        // ─── Multiple series ──────────────────────────────────────────────────────

        [Fact]
        public async Task Write_MultipleSeries_StoredSeparately()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            await _cache.WriteAsync(id1, MakeOrder("Series A", 2));
            await _cache.WriteAsync(id2, MakeOrder("Series B", 5));

            var r1 = await _cache.ReadAsync(id1);
            var r2 = await _cache.ReadAsync(id2);

            Assert.Equal("Series A", r1!.SeriesName);
            Assert.Equal(2, r1.Episodes.Count);
            Assert.Equal("Series B", r2!.SeriesName);
            Assert.Equal(5, r2.Episodes.Count);
        }
    }
}
