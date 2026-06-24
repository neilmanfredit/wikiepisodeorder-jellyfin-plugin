using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.ScheduledTasks
{
    public class RefreshWikipediaTask : IScheduledTask
    {
        private readonly RefreshService _refreshService;
        private readonly ILogger<RefreshWikipediaTask> _logger;

        public RefreshWikipediaTask(
            RefreshService refreshService,
            ILogger<RefreshWikipediaTask> logger)
        {
            _refreshService = refreshService;
            _logger = logger;
        }

        public string Name => "Refresh Wikipedia Episode Order";

        public string Key => "WikipediaEpisodeOrderRefresh";

        public string Description =>
            "Downloads and caches the latest episode order from Wikipedia for all configured series.";

        public string Category => "Wikipedia Episode Order";

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || config.Mappings.Count == 0)
            {
                _logger.LogInformation("No series mappings configured; nothing to refresh.");
                progress.Report(100);
                return;
            }

            var mappings = config.Mappings;
            _logger.LogInformation("RefreshWikipediaTask: refreshing {Count} series", mappings.Count);

            int processed = 0;
            foreach (var mapping in mappings)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    await _refreshService.RefreshSeriesAsync(mapping, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Refresh task cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing '{SeriesName}'", mapping.SeriesName);
                }

                processed++;
                progress.Report((double)processed / mappings.Count * 100);
            }

            // Persist updated LastUpdatedUtc timestamps
            if (Plugin.Instance != null)
            {
                Plugin.Instance.SaveConfiguration();
            }

            _logger.LogInformation("RefreshWikipediaTask complete.");
            progress.Report(100);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            };
        }
    }
}
