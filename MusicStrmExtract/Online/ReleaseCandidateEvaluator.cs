namespace MusicStrmExtract.Online;

/// <summary>拉取单个 release 详情并执行本地碟轨布局校验。</summary>
internal sealed class ReleaseCandidateEvaluator(IMusicBrainzApi api)
{
    private readonly IMusicBrainzApi _api = api ?? throw new ArgumentNullException(nameof(api));

    public async Task<ReleaseMatch?> TryMatchAsync(
        ReleaseSummary release,
        IReadOnlyList<LocalDisc> localDiscs,
        ISet<string> evaluatedReleaseIds,
        CancellationToken ct)
    {
        var releaseId = release.Id;
        if (string.IsNullOrWhiteSpace(releaseId) || evaluatedReleaseIds.Contains(releaseId))
            return null;

        var parsed = await _api.GetReleaseAsync(releaseId, ct).ConfigureAwait(false);
        _ = evaluatedReleaseIds.Add(releaseId);
        if (parsed.Medias.Count == 0)
            return null;

        var layout = ReleaseLayoutMatcher.TryMatch(localDiscs, parsed.Medias);
        return layout is null
            ? null
            : new ReleaseMatch(parsed.Release, parsed.Medias, layout.IsExact);
    }
}

/// <summary>单个 release 的详情与本地布局匹配结果。</summary>
internal sealed record ReleaseMatch(
    ReleaseSummary Release,
    IReadOnlyList<ReleaseMedia> Medias,
    bool IsExact);
