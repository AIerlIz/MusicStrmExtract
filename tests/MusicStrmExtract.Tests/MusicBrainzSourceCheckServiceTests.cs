using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicBrainzSourceCheckServiceTests
    {
        [Fact]
        public async Task CheckAsync_UsesConfiguredBaseUrlAndReportsCandidateCount()
        {
            string? receivedBaseUrl = null;
            var service = new MusicBrainzSourceCheckService(
                baseUrl =>
                {
                    receivedBaseUrl = baseUrl;
                    return new FakeApi();
                });

            var result = await service.CheckAsync(
                "https://musicbrainz.emby.tv",
                CancellationToken.None);

            Assert.Equal("https://musicbrainz.emby.tv", receivedBaseUrl);
            Assert.Contains("连接成功", result, StringComparison.Ordinal);
            Assert.Contains("1 条候选", result, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckAsync_UsesOfficialSourceWhenBaseUrlIsEmpty()
        {
            string? receivedBaseUrl = "unexpected";
            var service = new MusicBrainzSourceCheckService(
                baseUrl =>
                {
                    receivedBaseUrl = baseUrl;
                    return new FakeApi();
                });

            _ = await service.CheckAsync(string.Empty, CancellationToken.None);

            Assert.Null(receivedBaseUrl);
        }

        private sealed class FakeApi : IMusicBrainzApi
        {
            public Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
                string album,
                string? artist,
                int limit,
                CancellationToken ct)
            {
                return Task.FromResult<IReadOnlyList<ScoredRelease>>(
                    [
                        new ScoredRelease(
                            new ReleaseSummary(
                                "release-1",
                                "1989",
                                "2014-10-27",
                                "Official",
                                "US",
                                null,
                                null,
                                null,
                                "Album",
                                "rg-1",
                                [],
                                []),
                            100)
                    ]);
            }

            public Task<ParsedRelease> GetReleaseAsync(string releaseMbid, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task<ParsedReleaseGroup> GetReleaseGroupAsync(string rgMbid, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }
    }
}
