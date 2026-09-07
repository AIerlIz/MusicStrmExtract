using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online
{
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
            {
                return medias;
            }

            foreach (var m in mediaArr.EnumerateArray())
            {
                var tracks = new List<AlbumTrack>();
                if (m.TryGetProperty("tracks", out var tracksJson) && tracksJson.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tracksJson.EnumerateArray())
                    {
                        var numberText = GetString(t, "number");
                        var number = int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                            ? n
                            : GetInt(t, "position");
                        if (number <= 0)
                        {
                            continue;
                        }

                        var title = GetString(t, "title");
                        string? recordingMbid = null;
                        var artists = new List<string>();
                        string? artistMbid = null;
                        if (t.TryGetProperty("recording", out var rec))
                        {
                            recordingMbid = GetString(rec, "id");
                            title ??= GetString(rec, "title");
                            foreach (var credit in GetArtistCredits(rec, includeNameOnlyCredits: false))
                            {
                                if (!string.IsNullOrWhiteSpace(credit.Name))
                                {
                                    artists.Add(credit.Name!);
                                }

                                if (artistMbid is null && !string.IsNullOrWhiteSpace(credit.Id))
                                {
                                    artistMbid = credit.Id;
                                }
                            }
                        }

                        tracks.Add(new AlbumTrack(
                            number,
                            title,
                            recordingMbid,
                            artistMbid,
                            artists.ToArray()));
                    }
                }

                tracks.Sort((a, b) => a.Number.CompareTo(b.Number));
                if (tracks.Count > 0)
                {
                    medias.Add(new ReleaseMedia(GetInt(m, "position"), tracks.ToArray()));
                }
            }

            return medias;
        }
    }
}
