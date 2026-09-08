using System.Linq;
using System.Text.Json;

using MusicStrmExtract.Online;

namespace MusicStrmExtract.Tests
{
    internal static class TestReleaseJson
    {
        public static LocalDisc LocalDisc(int? discNumber, params int[] tracks)
        {
            var disc = new LocalDisc { DiscNumber = discNumber };
            disc.TrackNumbers.AddRange(tracks);
            return disc;
        }

        public static (int Number, string Title)[] Tracks(int fromNumber, int count)
        {
            return Enumerable.Range(fromNumber, count).Select(n => (n, $"歌{n}")).ToArray();
        }

        public static JsonElement BuildRelease(params (int Position, (int Number, string Title)[] Tracks)[] medias)
        {
            return BuildReleaseCore(medias
                .Select(m => new MediaInput(m.Position, null, m.Tracks))
                .ToArray());
        }

        public static JsonElement BuildReleaseWithFormats(
            params (int Position, string? Format, (int Number, string Title)[] Tracks)[] medias)
        {
            return BuildReleaseCore(medias
                .Select(m => new MediaInput(m.Position, m.Format, m.Tracks))
                .ToArray());
        }

        private static JsonElement BuildReleaseCore(MediaInput[] medias)
        {
            var sb = new System.Text.StringBuilder("{\"media\":[");
            for (var m = 0; m < medias.Length; m++)
            {
                if (m > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"position\":").Append(medias[m].Position);
                if (!string.IsNullOrWhiteSpace(medias[m].Format))
                {
                    sb.Append(",\"format\":\"").Append(medias[m].Format).Append('"');
                }

                sb.Append(",\"tracks\":[");
                for (var i = 0; i < medias[m].Tracks.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    var (num, title) = medias[m].Tracks[i];
                    sb.Append("{\"number\":\"").Append(num)
                      .Append("\",\"title\":\"").Append(title)
                      .Append("\",\"length\":200000,\"recording\":{\"id\":\"rec-")
                      .Append(medias[m].Position).Append('-').Append(num)
                      .Append("\",\"title\":\"").Append(title)
                      .Append("\",\"artist-credit\":[{\"artist\":{\"id\":\"art-1\",\"name\":\"周杰倫\"}}]}}");
                }

                sb.Append("]}");
            }

            sb.Append("]}");
            return JsonDocument.Parse(sb.ToString()).RootElement;
        }

        private readonly record struct MediaInput(
            int Position,
            string? Format,
            (int Number, string Title)[] Tracks);
    }
}
