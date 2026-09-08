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
    /// <summary>针对 SearchForTrackMapAsync 的选版链路测试(使用假 MusicBrainz,不联网)。</summary>
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

            var result = await RunAsync(api);

            Assert.True(result.Found);
            Assert.Contains(result.ReleaseMbid, new[] { "us1", "us2" });
            Assert.Equal("rg-1", result.ReleaseGroupMbid);
            Assert.Equal("Artist", result.ArtistName);
        }

        [Fact]
        public async Task SearchForTrackMapAsync_PicksDeterministicCandidateWhenTied()
        {
            // 两个 US 版本完全同分,不再请求 CAA,按稳定次序取 usA
            var api = new FakeMusicBrainzApi
            {
                SearchJson = SearchReleases("rg-1"),
                RgJson = RgReleases(("usA", "US", "AAA", "2014-10-27"), ("usB", "US", "AAA", "2014-10-27"))
            };
            api.ReleaseDetails["usA"] = ReleaseDetail("usA", "1989", "US", 13, "2014-10-27");
            api.ReleaseDetails["usB"] = ReleaseDetail("usB", "1989", "US", 13, "2014-10-27");

            var result = await RunAsync(api);

            Assert.True(result.Found);
            Assert.Equal("usA", result.ReleaseMbid);
        }

        [Fact]
        public async Task SearchForTrackMapAsync_MissingDatesOnBothSides_PicksStableCandidate()
        {
            // 双方同分且都缺日期时,不依赖封面图,按 release id 稳定取先者。
            var api = new FakeMusicBrainzApi
            {
                SearchJson = SearchReleases("rg-1"),
                RgJson = RgReleases(("noDateA", "US", "AAA", ""), ("noDateB", "US", "AAA", ""))
            };
            api.ReleaseDetails["noDateA"] = ReleaseDetail("noDateA", "1989", "US", 13, "");
            api.ReleaseDetails["noDateB"] = ReleaseDetail("noDateB", "1989", "US", 13, "");

            var result = await RunAsync(api);

            Assert.True(result.Found);
            Assert.Equal("noDateA", result.ReleaseMbid);
        }

        [Fact]
        public async Task SearchForTrackMapAsync_KeepsPreferredYear_WhenSameScore()
        {
            // 同国、同分、但年份不同:年份就近 → 原版优先,不因封面图而改变
            var api = new FakeMusicBrainzApi
            {
                SearchJson = SearchReleases("rg-1"),
                RgJson = RgReleases(("orig", "US", "AAA", "2014-08-03"), ("reissue", "US", "AAA", "2018-06-15"))
            };
            api.ReleaseDetails["orig"] = ReleaseDetail("orig", "1989", "US", 13, "2014-08-03");
            api.ReleaseDetails["reissue"] = ReleaseDetail("reissue", "1989", "US", 13, "2018-06-15");

            var result = await RunAsync(api);

            Assert.True(result.Found);
            Assert.Equal("orig", result.ReleaseMbid);
        }

        [Fact]
        public async Task SearchForTrackMapAsync_StopsAfterExactTier_WhenNoEquivalentCandidate()
        {
            var api = new FakeMusicBrainzApi
            {
                SearchJson = SearchReleases("rg-1"),
                RgJson = RgReleases(("top", "US", "AAA", "2014-10-27"), ("later", "US", "AAA", "2018-06-15"))
            };
            api.ReleaseDetails["top"] = ReleaseDetail("top", "1989", "US", 13, "2014-10-27");

            var result = await RunAsync(api);

            Assert.True(result.Found);
            Assert.Equal("top", result.ReleaseMbid);
            Assert.Equal(1, api.ReleaseDetailCalls);
        }

        [Fact]
        public async Task SearchForTrackMapAsync_PrefersExactReleaseFromAnotherReleaseGroup()
        {
            var api = new FakeMusicBrainzApi
            {
                SearchJson =
                    "{\"releases\":[" +
                    SearchReleaseJson("top-rg1", "rg-1", 100) + "," +
                    SearchReleaseJson("other-rg2", "rg-2", 90) +
                    "]}",
                RgJson = RgReleases(("top-rg1", "US", "AAA", "2014-10-27"), ("alt-rg1", "US", "BBB", "2014-10-27"))
            };
            api.ReleaseDetails["top-rg1"] = ReleaseDetail("top-rg1", "1989", "US", 14, "2014-10-27");
            api.ReleaseDetails["alt-rg1"] = ReleaseDetail("alt-rg1", "1989", "US", 14, "2014-10-27");
            api.ReleaseDetails["other-rg2"] = ReleaseDetail("other-rg2", "1989", "GB", 13, "2014-10-27");

            var result = await RunAsync(api);

            Assert.True(result.Found);
            Assert.Equal("other-rg2", result.ReleaseMbid);
        }

        [Fact]
        public async Task SearchForTrackMapAsync_ChecksAllSearchCandidatesBeyondFirstFive()
        {
            var api = new FakeMusicBrainzApi
            {
                SearchJson = "{\"releases\":[" +
                    SearchReleaseJson("c1", "rg-1", 100) + "," +
                    SearchReleaseJson("c2", "rg-2", 99) + "," +
                    SearchReleaseJson("c3", "rg-3", 98) + "," +
                    SearchReleaseJson("c4", "rg-4", 97) + "," +
                    SearchReleaseJson("c5", "rg-5", 96) + "," +
                    SearchReleaseJson("exact", "rg-6", 95) +
                    "]}",
                RgJson = "{\"releases\":[]}"
            };
            api.ReleaseDetails["exact"] = ReleaseDetail("exact", "1989", "US", 13, "2014-10-27");

            var result = await RunAsync(api);

            Assert.True(result.Found);
            Assert.Equal("exact", result.ReleaseMbid);
        }

        [Fact]
        public async Task SearchForTrackMapAsync_PrefersCdWithVideoBonusOverWorldwideDigital()
        {
            var api = new FakeMusicBrainzApi
            {
                SearchJson = SearchReleases("rg-1"),
                RgJson = ChopinRgReleases()
            };
            api.ReleaseDetails["tw"] = ChopinReleaseDetail(
                "tw",
                "TW",
                "2005-10-31",
                (1, "CD", 12),
                (2, "VCD", 3));
            api.ReleaseDetails["jp"] = ChopinReleaseDetail(
                "jp",
                "JP",
                "2006-01-18",
                (1, "CD", 12));
            api.ReleaseDetails["dig24"] = ChopinReleaseDetail(
                "dig24",
                "XW",
                "2005-11-11",
                (1, "Digital Media", 12));
            api.ReleaseDetails["dig2024"] = ChopinReleaseDetail(
                "dig2024",
                "XW",
                "2024-01-05",
                (1, "Digital Media", 12));

            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 12));
            var result = await new AlbumSearch(api).SearchForTrackMapAsync(
                "11月的萧邦 (2005)",
                "周杰伦",
                new[] { local },
                CancellationToken.None);

            Assert.True(result.Found);
            Assert.Equal("tw", result.ReleaseMbid);
        }

        private static async Task<AlbumSearchResult> RunAsync(FakeMusicBrainzApi api)
        {
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 13));
            return await new AlbumSearch(api).SearchForTrackMapAsync(
                "1989 (2014)", "Artist", new[] { local }, CancellationToken.None).ConfigureAwait(false);
        }

        private static string ChopinRgReleases()
        {
            return "{\"id\":\"rg-1\",\"title\":\"11月的蕭邦\",\"primary-type\":\"Album\"," +
                "\"artist-credit\":[{\"artist\":{\"id\":\"art-1\",\"name\":\"周杰伦\"}}],\"releases\":[" +
                "{\"id\":\"tw\",\"title\":\"11月的蕭邦\",\"date\":\"2005-10-31\",\"status\":\"Official\",\"country\":\"TW\",\"barcode\":\"828767594125\",\"disambiguation\":null,\"packaging\":null,\"media\":[{\"format\":\"CD\",\"track-count\":12},{\"format\":\"VCD\",\"track-count\":3}]}," +
                "{\"id\":\"dig24\",\"title\":\"11月的蕭邦\",\"date\":\"2005-11-11\",\"status\":\"Official\",\"country\":\"XW\",\"barcode\":\"00602458942408\",\"disambiguation\":\"24 bit\",\"packaging\":\"None\",\"media\":[{\"format\":\"Digital Media\",\"track-count\":12}]}," +
                "{\"id\":\"dig2024\",\"title\":\"11月的蕭邦\",\"date\":\"2024-01-05\",\"status\":\"Official\",\"country\":\"XW\",\"barcode\":\"602458942392\",\"disambiguation\":null,\"packaging\":\"None\",\"media\":[{\"format\":\"Digital Media\",\"track-count\":12}]}," +
                "{\"id\":\"jp\",\"title\":\"11月的蕭邦\",\"date\":\"2006-01-18\",\"status\":\"Official\",\"country\":\"JP\",\"barcode\":\"4547366023589\",\"disambiguation\":null,\"packaging\":null,\"media\":[{\"format\":\"CD\",\"track-count\":12}]}" +
                "]}";
        }

        private static string ChopinReleaseDetail(
            string id,
            string country,
            string date,
            params (int Position, string Format, int TrackCount)[] medias)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"id\":\"").Append(id)
              .Append("\",\"title\":\"11月的蕭邦\",\"date\":\"").Append(date)
              .Append("\",\"country\":\"").Append(country)
              .Append("\",\"status\":\"Official\",\"media\":[");
            for (var m = 0; m < medias.Length; m++)
            {
                if (m > 0)
                {
                    sb.Append(',');
                }

                var media = medias[m];
                sb.Append("{\"position\":").Append(media.Position)
                  .Append(",\"format\":\"").Append(media.Format)
                  .Append("\",\"track-count\":").Append(media.TrackCount)
                  .Append(",\"tracks\":[");
                for (var n = 1; n <= media.TrackCount; n++)
                {
                    if (n > 1)
                    {
                        sb.Append(',');
                    }

                    sb.Append("{\"number\":\"").Append(n)
                      .Append("\",\"title\":\"歌").Append(media.Position).Append('-').Append(n)
                      .Append("\",\"recording\":{\"id\":\"rec-").Append(id).Append('-')
                      .Append(media.Position).Append('-').Append(n)
                      .Append("\",\"title\":\"歌").Append(media.Position).Append('-').Append(n)
                      .Append("\",\"artist-credit\":[{\"artist\":{\"id\":\"art-1\",\"name\":\"周杰伦\"}}]}}");
                }

                sb.Append("]}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string SearchReleaseJson(string id, string rgId, int score)
        {
            return $"{{\"id\":\"{id}\",\"score\":{score},\"title\":\"1989\",\"date\":\"2014-10-27\"," +
                   $"\"status\":\"Official\",\"country\":\"US\",\"artist-credit\":[]," +
                   $"\"release-group\":{{\"id\":\"{rgId}\"}}}}";
        }

        private static string SearchReleases(string rgId)
        {
            return $"{{\"releases\":[{SearchReleaseJson("sr-1", rgId, 100)}]}}";
        }

        private static string RgReleases(params (string Id, string Country, string Barcode, string Date)[] releases)
        {
            var sb = new System.Text.StringBuilder(
                "{\"id\":\"rg-1\",\"title\":\"1989\",\"primary-type\":\"Album\"," +
                "\"artist-credit\":[{\"artist\":{\"id\":\"art-1\",\"name\":\"Artist\"}}],\"releases\":[");
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

            public int ReleaseDetailCalls { get; private set; }

            public Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
                string album,
                string? artist,
                int limit,
                CancellationToken ct)
                => Task.FromResult<IReadOnlyList<ScoredRelease>>(
                    ReleaseJsonReader.ParseSearchReleases(Parse(SearchJson)));

            public Task<ParsedReleaseGroup> GetReleaseGroupAsync(
                string rgMbid,
                CancellationToken ct)
                => Task.FromResult(ReleaseJsonReader.ParseReleaseGroup(Parse(RgJson)));

            public Task<ParsedRelease> GetReleaseAsync(string releaseMbid, CancellationToken ct)
            {
                ReleaseDetailCalls++;
                return Task.FromResult(ReleaseTracklistParser.ParseRelease(Parse(
                    ReleaseDetails.TryGetValue(releaseMbid, out var json) ? json : "{\"media\":[]}")));
            }

            public void Dispose()
            {
            }

            private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
        }

    }
}
