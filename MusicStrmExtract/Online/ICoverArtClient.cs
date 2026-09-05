using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    /// <summary>Cover Art Archive 封面查询能力抽象;便于选版逻辑脱离网络单测。</summary>
    public interface ICoverArtClient
    {
        /// <summary>查询某 release 的封面分(有正面 +10000,再加图数);不可达/异常返回 0。</summary>
        Task<int> GetCoverArtCountAsync(string releaseMbid, CancellationToken ct);
    }
}
