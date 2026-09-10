namespace MusicStrmExtract.Online;

/// <summary>把已匹配 release 与其 media 构造成不可变专辑结果，并保留 release-group 级 fallback。</summary>
internal static class AlbumSearchResultFactory
{
    public static AlbumSearchResult Create(
        ReleaseSummary release,
        IReadOnlyList<ReleaseMedia> medias,
        ParsedReleaseGroup? releaseGroup)
    {
        var artistCredits = release.ArtistCredits.Count > 0
            ? release.ArtistCredits
            : releaseGroup?.ArtistCredits ?? [];

        return new AlbumSearchResult(
            true,
            release.Title ?? releaseGroup?.Title,
            JsonUtil.ParseYear(release.Date),
            release.Id,
            release.ReleaseGroupMbid ?? releaseGroup?.Id,
            artistCredits
                .Select(c => c.Name)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
            artistCredits
                .Select(c => c.Id)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
            [.. medias]);
    }
}
