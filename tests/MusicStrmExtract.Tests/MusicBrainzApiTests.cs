using System;
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
            Assert.DoesNotContain("Chou%22%22", transport.LastUrl);
            Assert.Contains("/ws/2/release?query=", transport.LastUrl);
        }

        private sealed class FakeTransport : IHttpTransport
        {
            public int StatusCode { get; set; } = 200;

            public string Body { get; set; } = "{}";

            public int Calls { get; private set; }

            public string? LastUrl { get; private set; }

            public Task<HttpResponse> GetAsync(string url, CancellationToken ct)
            {
                Calls++;
                LastUrl = url;
                return Task.FromResult(new HttpResponse(StatusCode, Body));
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
