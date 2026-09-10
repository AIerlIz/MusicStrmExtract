using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;

namespace MusicStrmExtract.Ui;

/// <summary>根据库快照计算出的保守修复计划。</summary>
internal sealed record RepairPlan(
    IReadOnlyList<MusicAlbum> AlbumsToDelete,
    IReadOnlyList<Audio> StrmToRefresh);

/// <summary>只根据 Audio/MusicAlbum 快照计算删除与刷新目标,不访问 Emby 服务。</summary>
internal static class RepairPlanBuilder
{
    public static RepairPlan Build(
        IReadOnlyList<Audio> audios,
        IReadOnlyList<MusicAlbum> albums)
    {
        ArgumentNullException.ThrowIfNull(audios);
        ArgumentNullException.ThrowIfNull(albums);

        var referencedAlbumIds = audios
            .Where(a => a.AlbumId != 0)
            .Select(a => a.AlbumId)
            .ToHashSet();

        var albumsToDelete = albums
            .Where(a => !HasMusicBrainzAlbum(a)
                && string.IsNullOrWhiteSpace(a.Path)
                && !referencedAlbumIds.Contains(a.InternalId))
            .ToList();

        var staleReferencedAlbumIds = albums
            .Where(a => !HasMusicBrainzAlbum(a) && referencedAlbumIds.Contains(a.InternalId))
            .Select(a => a.InternalId)
            .ToHashSet();

        var strmToRefresh = audios
            .Where(a => a.Path?.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) == true
                && (staleReferencedAlbumIds.Contains(a.AlbumId)
                    || (a.AlbumId == 0 && HasMusicBrainzAlbum(a))))
            .ToList();

        return new RepairPlan(albumsToDelete, strmToRefresh);
    }

    internal static bool HasMusicBrainzAlbum(BaseItem item)
    {
        return item.ProviderIds != null
            && item.ProviderIds.TryGetValue(PluginConstants.MusicBrainzAlbum, out var id)
            && !string.IsNullOrWhiteSpace(id);
    }
}
