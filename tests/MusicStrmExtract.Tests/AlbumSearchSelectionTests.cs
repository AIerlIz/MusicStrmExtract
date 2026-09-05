using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    /// <summary>针对 SearchForTrackMapAsync 的选版链路测试(使用假 MusicBrainz/CoverArt,不联网)。</summary>
    public class AlbumSearchSelectionTests
    {
        [Fact]
        public async Task SearchForTrackMapAsync_PicksMajorityCountry()
        {
            // US 出现 2 次最多(AR 1 次),应选出 US 版本
            var api = new FakeMusicBrainzApi
            {
                SearchJson = SearchReleases("rg-1"),
                RgJson = RgReleases(("us1", "US", "AAA", "2014-10-27"), ("us2", "US", "BBB", "2014-10-27"), ("ar1", "AR", "CCC", "2014-10-27"))
            };
            api.ReleaseDetails["us1"] = ReleaseDetail("us1", "1989", "US", 13, "2014-10-27");
            api.ReleaseDetails["us2"] = ReleaseDetail("us2", "1989", "US", 13, "2014-10-27");
            api.ReleaseDetails["ar1"] = ReleaseDetail("ar1", "1989", "AR", 13, "2014-10-27");

            var result = await RunAsync(api, new FakeCoverArtClient());

            Assert.True(result.Found);
            Assert.Contains(result.ReleaseMbid, new[] { "us1", "us2" });
        }

        [Fact]
        public async Task SearchForTrackMapAsync_CaaBreaksTiedCandidates()
        {
            // 两个 US 版本同分,靠 Cover Art Archive 封面数(usA 9 图、usB 3 图)决胜
            var api = new FakeMusicBrainzApi
            {
                SearchJson = SearchReleases("rg-1"),
                RgJson = RgReleases(("usA", "US", "AAA", "2014-10-27"), ("usB", "US", "AAA", "2014-10-27"))
            };
            api.ReleaseDetails["usA"] = ReleaseDetail("usA", "1989", "US", 13, "2014-10-27");
            api.ReleaseDetails["usB"] = ReleaseDetail("usB", "1989", "US", 13, "2014-10-27");

            var cover = new FakeCoverArtClient();
            cover.Counts["usA"] = 10009; // 有正面 + 9 图
            cover.Counts["usB"] = 3;

            var result = await RunAsync(api, cover);

            Assert.True(result.Found);
            Assert.Equal("usA", result.ReleaseMbid);
        }

        [Fact]
        public async Task SearchForTrackMapAsync_KeepsPreferredYear_WhenSameScore()
        {
            // 同国、同分、但年份不同:CAA 不应覆盖"年份就近 → 原版优先"的排序意图
            var api = new FakeMusicBrainzApi
            {
                SearchJson = SearchReleases("rg-1"),
                RgJson = RgReleases(("orig", "US", "AAA", "2014-08-03"), ("reissue", "US", "AAA", "2018-06-15"))
            };
            api.ReleaseDetails["orig"] = ReleaseDetail("orig", "1989", "US", 13, "2014-08-03");
            api.ReleaseDetails["reissue"] = ReleaseDetail("reissue", "1989", "US", 13, "2018-06-15");

            var cover = new FakeCoverArtClient();
            cover.Counts["orig"] = 2;        // 原版封面更少
            cover.Counts["reissue"] = 10009; // 重版封面更多

            var result = await RunAsync(api, cover);

            Assert.True(result.Found);
            Assert.Equal("orig", result.ReleaseMbid);
        }

        private static async Task<AlbumSearchResult> RunAsync(FakeMusicBrainzApi api, FakeCoverArtClient cover)
        {
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 13));
            return await new AlbumSearch(api, cover).SearchForTrackMapAsync(
                "1989 (2014)", "Artist", new[] { local }, CancellationToken.None).ConfigureAwait(false);
        }

        private static string SearchReleases(string rgId)
        {
            return $"{{\"releases\":[{{\"id\":\"sr-1\",\"score\":100,\"title\":\"1989\",\"date\":\"2014-10-27\",\"status\":\"Official\",\"country\":\"US\",\"release-group\":{{\"id\":\"{rgId}\"}}}}]}}";
        }

        private static string RgReleases(params (string Id, string Country, string Barcode, string Date)[] releases)
        {
            var sb = new System.Text.StringBuilder("{\"releases\":[");
            for (var i = 0; i < releases.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var (id, country, barcode, date) = releases[i];
                sb.Append($"{{\"id\":\"{id}\",\"title\":\"1989\",\"date\":\"{date}\",\"status\":\"Official\",\"country\":\"{country}\",\"barcode\":\"{barcode}\",\"disambiguation\":null,\"packaging\":\"Jewel Case\",\"media\":[{{\"format\":\"CD\",\"track-count\":13}}]}}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string ReleaseDetail(string id, string title, string country, int trackCount, string date = "2014-10-27")
        {
            var tracks = new System.Text.StringBuilder();
            for (var n = 1; n <= trackCount; n++)
            {
                if (n > 1) tracks.Append(',');
                tracks.Append($"{{\"number\":\"{n}\",\"title\":\"{title} {n}\",\"recording\":{{\"id\":\"rec-{id}-{n}\",\"title\":\"{title} {n}\",\"artist-credit\":[{{\"artist\":{{\"id\":\"art-1\",\"name\":\"Artist\"}}}}]}}}}");
            }

            return $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"date\":\"{date}\",\"country\":\"{country}\",\"status\":\"Official\",\"media\":[{{\"position\":1,\"track-count\":{trackCount},\"tracks\":[{tracks}]}}]}}";
        }

        private sealed class FakeMusicBrainzApi : IMusicBrainzApi
        {
            public string SearchJson { get; set; } = "{\"releases\":[]}";

            public string RgJson { get; set; } = "{\"releases\":[]}";

            public Dictionary<string, string> ReleaseDetails { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            public Task<JsonElement> SearchReleasesAsync(string album, string? artist, int limit, CancellationToken ct)
                => Task.FromResult(Parse(SearchJson));

            public Task<JsonElement> GetReleaseGroupReleasesAsync(string rgMbid, CancellationToken ct)
                => Task.FromResult(Parse(RgJson));

            public Task<JsonElement> GetReleaseAsync(string releaseMbid, CancellationToken ct)
                => Task.FromResult(Parse(ReleaseDetails.TryGetValue(releaseMbid, out var json) ? json : "{\"media\":[]}"));

            public Task<JsonElement> GetRecordingAsync(string recordingMbid, CancellationToken ct)
                => Task.FromResult(Parse("{}"));

            public Task<JsonElement> SearchRecordingsAsync(string title, int limit, CancellationToken ct)
                => Task.FromResult(Parse("{\"recordings\":[]}"));

            public void Dispose()
            {
            }

            private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
        }

        private sealed class FakeCoverArtClient : ICoverArtClient
        {
            public Dictionary<string, int> Counts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

            public Task<int> GetCoverArtCountAsync(string releaseMbid, CancellationToken ct)
                => Task.FromResult(Counts.TryGetValue(releaseMbid, out var count) ? count : 0);
        }
    }
}
