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
            var sb = new System.Text.StringBuilder("{\"media\":[");
            for (var m = 0; m < medias.Length; m++)
            {
                if (m > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"position\":").Append(medias[m].Position).Append(",\"tracks\":[");
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

        public static JsonElement BuildCoverArt(int count, bool front)
        {
            var sb = new System.Text.StringBuilder("{\"images\":[");
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(front && i == 0 ? "{\"front\":true}" : "{\"front\":false}");
            }

            sb.Append("]}");
            return JsonDocument.Parse(sb.ToString()).RootElement;
        }
    }
}
