using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Model.Logging;

using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class AlbumTrackMapLocatorTests
    {
        [Fact]
        public async Task GetOrSearchAsync_SingleFlightsConcurrentRequestsForSameAlbum()
        {
            var cache = new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 10);
            var api = new CountingMusicBrainzApi();
            var locator = new AlbumTrackMapLocator(
                CreateLogger(),
                cache,
                _ => api);
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 10));
            var config = new PluginConfiguration();

            var tasks = Enumerable.Range(0, 8)
                .Select(_ => locator.GetOrSearchAsync(
                    "Artist|Album|1:1-10|official|official",
                    "Album",
                    "Artist",
                    new[] { local },
                    config,
                    CancellationToken.None))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.False(r.Found));
            Assert.Equal(1, api.SearchCalls);
            Assert.Equal(1, cache.Count);
        }

        private static ILogger CreateLogger()
        {
            return DispatchProxy.Create<ILogger, NoOpLogger>();
        }

        private sealed class CountingMusicBrainzApi : IMusicBrainzApi
        {
            private int _searchCalls;

            public int SearchCalls => Volatile.Read(ref _searchCalls);

            public async Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
                string album,
                string? artist,
                int limit,
                CancellationToken ct)
            {
                Interlocked.Increment(ref _searchCalls);
                await Task.Delay(100, ct).ConfigureAwait(false);
                return Array.Empty<ScoredRelease>();
            }

            public Task<ParsedRelease> GetReleaseAsync(string releaseMbid, CancellationToken ct)
            {
                return Task.FromResult(new ParsedRelease(
                    new ReleaseSummary(
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        Array.Empty<ArtistCredit>(),
                        Array.Empty<ReleaseMediaInfo>()),
                    Array.Empty<ReleaseMedia>()));
            }

            public Task<ParsedReleaseGroup> GetReleaseGroupAsync(string rgMbid, CancellationToken ct)
            {
                return Task.FromResult(new ParsedReleaseGroup(
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<ArtistCredit>(),
                    Array.Empty<ReleaseSummary>()));
            }

            public void Dispose()
            {
            }
        }

        private class NoOpLogger : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return null;
            }
        }
    }
}
