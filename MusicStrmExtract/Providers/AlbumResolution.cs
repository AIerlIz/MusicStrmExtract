using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers;

/// <summary>一次专辑定位请求的稳定输入。缓存键由服务实现内部维护。</summary>
internal sealed record AlbumResolutionRequest(
    string AlbumFolder,
    string? ArtistFolder,
    IReadOnlyList<LocalDisc> LocalDiscs,
    string? MusicBrainzBaseUrl);

/// <summary>专辑定位的缓存与搜索边界，供 Provider 编排测试替换。</summary>
internal interface IAlbumResolutionService
{
    Task<AlbumSearchResult> ResolveAsync(
        AlbumResolutionRequest request,
        CancellationToken ct);
}
