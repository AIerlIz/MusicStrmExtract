namespace MusicStrmExtract.Online;

/// <summary>把已匹配 release 与其 media 构造成不可变专辑结果，并保留 release-group 级 fallback。</summary>
internal static class AlbumSearchResultFactory
{
    public static AlbumSearchResult Create(
        ReleaseSummary release,
        IReadOnlyList<ReleaseMedia> medias,
        ParsedReleaseGroup? releaseGroup)
    {
        var artistCredits = SelectArtistCredits(release, releaseGroup);

        return new AlbumSearchResult(
            true,
            release.Title ?? releaseGroup?.Title,
            JsonUtil.ParseYear(release.Date),
            release.Id,
            release.ReleaseGroupMbid ?? releaseGroup?.Id,
            FirstNonEmpty(artistCredits, c => c.Name),
            FirstNonEmpty(artistCredits, c => c.Id),
            [.. medias]);
    }

    /// <summary>艺人信息以 release 级为准,缺失时回退到 release-group 级。</summary>
    private static IReadOnlyList<ArtistCredit> SelectArtistCredits(
        ReleaseSummary release,
        ParsedReleaseGroup? releaseGroup)
    {
        return release.ArtistCredits.Count > 0
            ? release.ArtistCredits
            : releaseGroup?.ArtistCredits ?? [];
    }

    private static string? FirstNonEmpty(
        IReadOnlyList<ArtistCredit> credits,
        Func<ArtistCredit, string?> selector)
    {
        return credits
            .Select(selector)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
