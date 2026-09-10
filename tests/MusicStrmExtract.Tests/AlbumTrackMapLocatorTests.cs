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
        public async Task ResolveAsync_SingleFlightsConcurrentRequestsForSameAlbum()
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
                .Select(_ => locator.ResolveAsync(
                    new AlbumResolutionRequest(
                        "Album",
                        "Artist",
                        new[] { local },
                        config.MusicBrainzBaseUrl),
                    CancellationToken.None))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.False(r.Found));
            Assert.Equal(1, api.SearchCalls);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public async Task ResolveAsync_OneWaiterCancellation_DoesNotCancelSharedWork()
        {
            var cache = new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 10);
            var api = new ControlledMusicBrainzApi();
            var locator = CreateLocator(cache, api);
            using var firstCts = new CancellationTokenSource();
            using var secondCts = new CancellationTokenSource();
            var request = CreateRequest();

            var first = locator.ResolveAsync(request, firstCts.Token);
            var second = locator.ResolveAsync(request, secondCts.Token);
            await api.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await firstCts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            Assert.False(api.CancellationObserved.Task.IsCompleted);
            api.ReleaseSearch.TrySetResult();

            var result = await second;

            Assert.False(result.Found);
            Assert.Equal(1, api.SearchCalls);
            Assert.Equal(1, cache.Count);
            Assert.False(api.CancellationObserved.Task.IsCompleted);
        }

        [Fact]
        public async Task ResolveAsync_LastWaiterCancellation_CancelsSharedWorkAndDoesNotCache()
        {
            var cache = new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 10);
            var api = new ControlledMusicBrainzApi();
            var locator = CreateLocator(cache, api);
            using var firstCts = new CancellationTokenSource();
            using var secondCts = new CancellationTokenSource();
            var request = CreateRequest();

            var first = locator.ResolveAsync(request, firstCts.Token);
            var second = locator.ResolveAsync(request, secondCts.Token);
            await api.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await firstCts.CancelAsync();
            await secondCts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            await api.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, api.SearchCalls);
            Assert.Equal(0, cache.Count);
        }

        private static AlbumTrackMapLocator CreateLocator(
            TtlCache<AlbumSearchResult> cache,
            IMusicBrainzApi api)
        {
            return new AlbumTrackMapLocator(CreateLogger(), cache, _ => api);
        }

        private static AlbumResolutionRequest CreateRequest()
        {
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 10));
            return new AlbumResolutionRequest(
                "Album",
                "Artist",
                [local],
                string.Empty);
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

        private sealed class ControlledMusicBrainzApi : IMusicBrainzApi
        {
            private int _searchCalls;

            public TaskCompletionSource SearchStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource ReleaseSearch { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource CancellationObserved { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int SearchCalls => Volatile.Read(ref _searchCalls);

            public async Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
                string album,
                string? artist,
                int limit,
                CancellationToken ct)
            {
                Interlocked.Increment(ref _searchCalls);
                SearchStarted.TrySetResult();
                try
                {
                    await ReleaseSearch.Task.WaitAsync(ct).ConfigureAwait(false);
                    return Array.Empty<ScoredRelease>();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    CancellationObserved.TrySetResult();
                    throw;
                }
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

        private class NoOpLogger : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return null;
            }
        }
    }
}
