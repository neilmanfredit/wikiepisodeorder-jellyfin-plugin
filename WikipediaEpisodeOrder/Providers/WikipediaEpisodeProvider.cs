using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Interfaces;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Providers
{
    public class WikipediaEpisodeProvider : IEpisodeOrderProvider
    {
        private readonly WikipediaParser _parser;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WikipediaEpisodeProvider> _logger;

        public WikipediaEpisodeProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<WikipediaEpisodeProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _parser = new WikipediaParser(logger);
        }

        public async Task<WikiSeriesOrder> GetEpisodeOrderAsync(string source, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Wikipedia URL must not be empty.", nameof(source));

            _logger.LogInformation("Downloading Wikipedia page: {Url}", source);

            var client = _httpClientFactory.CreateClient("WikipediaEpisodeOrder");
            string html;
            try
            {
                html = await client.GetStringAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to download Wikipedia page: {Url}", source);
                throw;
            }

            _logger.LogInformation("Downloaded {Length} bytes from {Url}", html.Length, source);

            var order = _parser.Parse(html, source);
            _logger.LogInformation(
                "Parsed {Count} episodes from {Url}",
                order.Episodes.Count,
                source);

            return order;
        }
    }
}
