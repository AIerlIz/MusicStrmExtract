using System.Collections.Generic;
using System.Globalization;
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

        public static List<ReleaseMedia> ParseReleaseMedias(JsonElement releaseRoot)
        {
            var medias = new List<ReleaseMedia>();
            if (!releaseRoot.TryGetProperty("media", out var mediaArr) || mediaArr.ValueKind != JsonValueKind.Array)
            {
                return medias;
            }

            foreach (var m in mediaArr.EnumerateArray())
            {
                var media = new ReleaseMedia { Position = GetInt(m, "position") };
                if (m.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tracks.EnumerateArray())
                    {
                        var track = new AlbumTrack();
                        var numberText = GetString(t, "number");
                        track.Number = int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                            ? n
                            : GetInt(t, "position");
                        track.Title = GetString(t, "title");
                        if (t.TryGetProperty("recording", out var rec))
                        {
                            track.RecordingMbid = GetString(rec, "id");
                            if (track.Title is null)
                            {
                                track.Title = GetString(rec, "title");
                            }

                            ApplyArtistCredit(track, rec);
                        }

                        if (track.Number > 0)
                        {
                            media.Tracks.Add(track);
                        }
                    }
                }

                media.Tracks.Sort((a, b) => a.Number.CompareTo(b.Number));
                if (media.Tracks.Count > 0)
                {
                    medias.Add(media);
                }
            }

            return medias;
        }

        private static void ApplyArtistCredit(AlbumTrack track, JsonElement recording)
        {
            foreach (var credit in GetArtistCredits(recording, includeNameOnlyCredits: false))
            {
                if (!string.IsNullOrWhiteSpace(credit.Name))
                {
                    track.Artists.Add(credit.Name!);
                }

                if (track.ArtistMbid is null && !string.IsNullOrWhiteSpace(credit.Id))
                {
                    track.ArtistMbid = credit.Id;
                }
            }
        }
    }
}
