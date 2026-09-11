using System.Globalization;
using System.Text.Json;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online;

/// <summary>把 MB release 响应 (inc=recordings) 解析成轨道映射所需的 media 列表。</summary>
internal static class ReleaseTracklistParser
{
    public static ParsedRelease ParseRelease(JsonElement releaseRoot)
    {
        return new ParsedRelease(
            ReleaseJsonReader.ParseRelease(releaseRoot),
            ParseReleaseMedias(releaseRoot));
    }

    public static IReadOnlyList<ReleaseMedia> ParseReleaseMedias(JsonElement releaseRoot)
    {
        var medias = new List<ReleaseMedia>();
        if (!releaseRoot.TryGetProperty("media", out var mediaArr) || mediaArr.ValueKind != JsonValueKind.Array)
            return medias;

        foreach (var media in mediaArr.EnumerateArray())
        {
            var tracks = ParseTracks(media);
            if (tracks.Count == 0)
                continue;

            medias.Add(new ReleaseMedia(
                GetInt(media, "position"),
                GetString(media, "format"),
                tracks));
        }

        return medias;
    }

    /// <summary>解析单张 media 的轨道并按轨号升序返回;无有效轨道时返回空列表。</summary>
    private static List<AlbumTrack> ParseTracks(JsonElement media)
    {
        var tracks = new List<AlbumTrack>();
        if (!media.TryGetProperty("tracks", out var tracksJson) || tracksJson.ValueKind != JsonValueKind.Array)
            return tracks;

        foreach (var track in tracksJson.EnumerateArray())
        {
            var parsed = ParseTrack(track);
            if (parsed is not null)
                tracks.Add(parsed);
        }

        tracks.Sort((a, b) => a.Number.CompareTo(b.Number));
        return tracks;
    }

    /// <summary>解析单条轨道;轨号缺失或非正时返回 null(该轨跳过)。</summary>
    private static AlbumTrack? ParseTrack(JsonElement track)
    {
        var number = ResolveTrackNumber(track);
        if (number <= 0)
            return null;

        var title = GetString(track, "title");
        string? recordingMbid = null;
        string? artistMbid = null;
        var artists = new List<string>();

        if (track.TryGetProperty("recording", out var recording))
        {
            recordingMbid = GetString(recording, "id");
            title ??= GetString(recording, "title");

            foreach (var credit in GetArtistCredits(recording, includeNameOnlyCredits: false))
            {
                if (!string.IsNullOrWhiteSpace(credit.Name))
                    artists.Add(credit.Name!);

                if (artistMbid is null && !string.IsNullOrWhiteSpace(credit.Id))
                    artistMbid = credit.Id;
            }
        }

        return new AlbumTrack(number, title, recordingMbid, artistMbid, [.. artists]);
    }

    /// <summary>轨号优先取 number 字符串,回退到 position 字段。</summary>
    private static int ResolveTrackNumber(JsonElement track)
    {
        var numberText = GetString(track, "number");
        return int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : GetInt(track, "position");
    }
}
