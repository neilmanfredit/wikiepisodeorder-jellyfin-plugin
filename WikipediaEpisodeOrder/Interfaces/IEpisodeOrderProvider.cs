using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WikipediaEpisodeOrder.Models;

namespace Jellyfin.Plugin.WikipediaEpisodeOrder.Interfaces
{
    public interface IEpisodeOrderProvider
    {
        Task<WikiSeriesOrder> GetEpisodeOrderAsync(string source, CancellationToken cancellationToken = default);
    }
}
