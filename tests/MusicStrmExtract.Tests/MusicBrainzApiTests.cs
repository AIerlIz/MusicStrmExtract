using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicBrainzApiTests
    {
        [Fact]
        public async Task GetReleaseAsync_ParsesTypedResultAndCachesSuccessfulResponse()
        {
            var transport = new FakeTransport
            {
                Body = "{\"id\":\"release-1\",\"title\":\"Album\",\"date\":\"2020-01-01\"," +
                       "\"media\":[{\"position\":1,\"track-count\":1,\"tracks\":[" +
                       "{\"number\":\"1\",\"title\":\"Song\",\"recording\":{\"id\":\"rec-1\",\"title\":\"Song\"}}]}]}"
            };
            var gate = new CountingGate();
            using var api = new MusicBrainzApi("https://mb.example", transport, gate);

            var first = await api.GetReleaseAsync("release-1", CancellationToken.None);
            var second = await api.GetReleaseAsync("release-1", CancellationToken.None);

            Assert.Equal("release-1", first.Release.Id);
            Assert.Equal("Album", first.Release.Title);
            var media = Assert.Single(first.Medias);
            Assert.Equal("Song", Assert.Single(media.Tracks).Title);
            Assert.Equal("rec-1", Assert.Single(media.Tracks).RecordingMbid);
            Assert.Equal(first.Release.Id, second.Release.Id);
            Assert.Equal(first.Medias.Count, second.Medias.Count);
            Assert.Equal(first.Medias[0].Tracks[0].Title, second.Medias[0].Tracks[0].Title);
            Assert.Equal(1, transport.Calls);
            Assert.Equal(1, gate.AcquireCount);
            Assert.Contains("/ws/2/release/release-1?inc=", transport.LastUrl);
        }

        [Fact]
        public async Task HttpError_ThrowsAndDoesNotCacheFailure()
        {
            var transport = new FakeTransport
            {
                StatusCode = 503,
                Body = "temporarily unavailable"
            };
            var gate = new CountingGate();
            using var api = new MusicBrainzApi("https://mb.example", transport, gate);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => api.GetReleaseAsync("release-1", CancellationToken.None));
            await Assert.ThrowsAsync<HttpRequestException>(
                () => api.GetReleaseAsync("release-1", CancellationToken.None));

            Assert.Equal(2, transport.Calls);
            Assert.Equal(2, gate.AcquireCount);
        }

        [Fact]
        public async Task SearchReleasesAsync_RemovesQuotesFromQueryAndParsesScore()
        {
            var transport = new FakeTransport
            {
                Body = "{\"releases\":[{\"id\":\"release-1\",\"score\":88,\"title\":\"1989\"," +
                       "\"date\":\"2014-10-27\",\"status\":\"Official\",\"artist-credit\":[]," +
                       "\"release-group\":{\"id\":\"rg-1\"}}]}"
            };
            var gate = new CountingGate();
            using var api = new MusicBrainzApi("https://mb.example", transport, gate);

            var releases = await api.SearchReleasesAsync("1989", "Jay \"Chou\"", 10, CancellationToken.None);

            var scored = Assert.Single(releases);
            Assert.Equal(88, scored.Score);
            Assert.Equal("release-1", scored.Release.Id);
            // 引号改为转义(%5C%22)而非删除;不得出现旧实现的连续 %22%22
            Assert.DoesNotContain("Chou%22%22", transport.LastUrl);
            Assert.Contains("Jay%20%5C%22Chou%5C%22", transport.LastUrl);
            Assert.Contains("/ws/2/release?query=", transport.LastUrl);
        }

        [Fact]
        public async Task GetReleaseGroupAsync_ParsesGroupConceptAndReleases()
        {
            var transport = new FakeTransport
            {
                Body = "{\"id\":\"rg-1\",\"title\":\"1989\",\"primary-type\":\"Album\"," +
                       "\"artist-credit\":[{\"artist\":{\"id\":\"artist-1\",\"name\":\"Taylor Swift\"}}]," +
                       "\"releases\":[{\"id\":\"release-1\",\"title\":\"1989\",\"status\":\"Official\"," +
                       "\"country\":\"US\",\"barcode\":\"843930013500\",\"media\":[" +
                       "{\"position\":1,\"format\":\"CD\",\"track-count\":13}]}]}"
            };
            var gate = new CountingGate();
            using var api = new MusicBrainzApi("https://mb.example", transport, gate);

            var group = await api.GetReleaseGroupAsync("rg-1", CancellationToken.None);

            Assert.Equal("rg-1", group.Id);
            Assert.Equal("1989", group.Title);
            Assert.Equal("Album", group.PrimaryType);
            Assert.Equal("Taylor Swift", group.ArtistCredits[0].Name);
            var release = Assert.Single(group.Releases);
            Assert.Equal("843930013500", release.Barcode);
            Assert.Contains("/ws/2/release-group/rg-1?inc=releases+media+artist-credits", transport.LastUrl);
            Assert.Equal(1, gate.AcquireCount);
        }

        [Fact]
        public async Task GetReleaseGroupAsync_PaginatesPastTwentyFiveLinkedReleases()
        {
            var firstPage = string.Join(",", Enumerable.Range(0, 25)
                .Select(n => ReleaseJson($"r{n}")));
            var secondPage = string.Join(",", Enumerable.Range(25, 5)
                .Select(n => ReleaseJson($"r{n}")));
            var transport = new FakeTransport
            {
                UrlBody = url => url.Contains("/release-group/", StringComparison.Ordinal)
                    ? $"{{\"id\":\"rg-1\",\"title\":\"Album\",\"primary-type\":\"Album\",\"artist-credit\":[],\"releases\":[{firstPage}]}}"
                    : $"{{\"count\":30,\"offset\":25,\"releases\":[{secondPage}]}}"
            };
            var gate = new CountingGate();
            using var api = new MusicBrainzApi("https://mb.example", transport, gate);

            var group = await api.GetReleaseGroupAsync("rg-1", CancellationToken.None);

            Assert.Equal(30, group.Releases.Count);
            Assert.Equal("r0", group.Releases[0].Id);
            Assert.Equal("r29", group.Releases[^1].Id);
            Assert.Equal(2, transport.Calls);
            Assert.Equal(2, gate.AcquireCount);
            Assert.Contains("release?release-group=rg-1", transport.LastUrl);
        }

        [Fact]
        public async Task GetReleaseGroupAsync_RepeatedPageWithoutNewReleases_Terminates()
        {
            // 服务端恒返回同一批 25 条已见过的 release 且 count 虚高:
            // 修复前 seen 去重使结果集不增长、两终止条件均不满足 → while(true) 死循环 + 无限 HTTP。
            // 本测试在修复前会挂死,修复后(本页未新增即退出)必须有限时间内返回。
            var samePage = string.Join(",", Enumerable.Range(0, 25).Select(n => ReleaseJson($"r{n}")));
            var transport = new FakeTransport
            {
                UrlBody = url => url.Contains("/release-group/", StringComparison.Ordinal)
                    ? $"{{\"id\":\"rg-1\",\"title\":\"Album\",\"primary-type\":\"Album\",\"artist-credit\":[],\"releases\":[{samePage}]}}"
                    : $"{{\"count\":1000,\"offset\":25,\"releases\":[{samePage}]}}"
            };
            var gate = new CountingGate();
            using var api = new MusicBrainzApi("https://mb.example", transport, gate);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var group = await api.GetReleaseGroupAsync("rg-1", cts.Token);

            // 未新增任何 release → 第一页 browse 后即退出;不得无限分页
            Assert.Equal(25, group.Releases.Count);
            Assert.Equal(2, transport.Calls); // 1 次 lookup + 1 次 browse
        }

        [Fact]
        public async Task GetReleaseGroupAsync_ZeroTotalCount_StopsAfterFirstPage()
        {
            // count=0(服务端显式给出总数 0):releases.Count(25) >= 0 → 终止,不得继续分页。
            // 本测试是"count=0 不得引发无限分页"的回归护栏(旧实现同样会停,属非鉴别性护栏)。
            var firstPage = string.Join(",", Enumerable.Range(0, 25).Select(n => ReleaseJson($"r{n}")));
            var transport = new FakeTransport
            {
                UrlBody = url => url.Contains("/release-group/", StringComparison.Ordinal)
                    ? $"{{\"id\":\"rg-1\",\"title\":\"Album\",\"primary-type\":\"Album\",\"artist-credit\":[],\"releases\":[{firstPage}]}}"
                    : $"{{\"count\":0,\"offset\":25,\"releases\":[]}}"
            };
            var gate = new CountingGate();
            using var api = new MusicBrainzApi("https://mb.example", transport, gate);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var group = await api.GetReleaseGroupAsync("rg-1", cts.Token);

            Assert.Equal(25, group.Releases.Count);
            Assert.Equal(2, transport.Calls);
        }

        [Fact]
        public async Task GetReleaseGroupAsync_MissingCountField_DoesNotDropLaterPages()
        {
            // 响应无 count 字段:旧实现 GetInt 返回 0 → releases.Count >= 0 恒真 →
            // 第 1 个 browse 页后立即退出,静默丢弃后续页(丢数据)。
            // 新实现 total 未知时不作为终止依据,应继续按 offset 翻页直到空页。
            var page0 = string.Join(",", Enumerable.Range(0, 25).Select(n => ReleaseJson($"r{n}")));
            var page1 = string.Join(",", Enumerable.Range(25, 25).Select(n => ReleaseJson($"r{n}")));
            var page2 = string.Join(",", Enumerable.Range(50, 5).Select(n => ReleaseJson($"r{n}")));
            var transport = new FakeTransport
            {
                UrlBody = url => url.Contains("/release-group/", StringComparison.Ordinal)
                    ? $"{{\"id\":\"rg-1\",\"title\":\"Album\",\"primary-type\":\"Album\",\"artist-credit\":[],\"releases\":[{page0}]}}"
                    : url.Contains("offset=25", StringComparison.Ordinal)
                        ? $"{{\"offset\":25,\"releases\":[{page1}]}}"                 // 无 count 字段
                        : url.Contains("offset=50", StringComparison.Ordinal)
                            ? $"{{\"offset\":50,\"releases\":[{page2}]}}"             // 无 count 字段
                            : "{\"offset\":55,\"releases\":[]}"
            };
            var gate = new CountingGate();
            using var api = new MusicBrainzApi("https://mb.example", transport, gate);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var group = await api.GetReleaseGroupAsync("rg-1", cts.Token);

            // 缺失 count 不截断后续页,全部 55 条都保留
            Assert.Equal(55, group.Releases.Count);
            Assert.Equal("r0", group.Releases[0].Id);
            Assert.Equal("r54", group.Releases[^1].Id);
        }

        private static string ReleaseJson(string id)
        {
            return $"{{\"id\":\"{id}\",\"title\":\"Album\",\"date\":\"2020-01-01\"," +
                   $"\"status\":\"Official\",\"country\":\"US\",\"artist-credit\":[],\"media\":[]}}";
        }

        private sealed class FakeTransport : IHttpTransport
        {
            public int StatusCode { get; set; } = 200;

            public string Body { get; set; } = "{}";

            public int Calls { get; private set; }

            public string? LastUrl { get; private set; }

            /// <summary>按 URL 决定响应体(与调用序号无关的旧用法)。</summary>
            public Func<string, string>? UrlBody { get; set; }

            /// <summary>按 (URL, 本次调用序号) 决定响应体,优先级高于 <see cref="UrlBody"/>。</summary>
            public Func<string, int, string>? UrlBodyWithCallIndex { get; set; }

            public Task<HttpResponse> GetAsync(string url, CancellationToken ct)
            {
                Calls++;
                LastUrl = url;
                var body = UrlBodyWithCallIndex?.Invoke(url, Calls)
                    ?? UrlBody?.Invoke(url)
                    ?? Body;
                return Task.FromResult(new HttpResponse(StatusCode, body));
            }

            public void Dispose()
            {
            }
        }

        private sealed class CountingGate : IRequestGate
        {
            public int AcquireCount { get; private set; }

            public Task<IDisposable> AcquireAsync(CancellationToken ct)
            {
                AcquireCount++;
                return Task.FromResult<IDisposable>(new Lease());
            }

            private sealed class Lease : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
