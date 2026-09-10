using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ReleaseCandidateEvaluatorTests
    {
        [Fact]
        public async Task TryMatchAsync_EvaluatesReleaseOnlyOnceAndMatchesLayout()
        {
            var release = CreateRelease("release-1");
            var api = new FakeApi
            {
                Release = new ParsedRelease(
                    release,
                    [new ReleaseMedia(1, "CD", [new AlbumTrack(1, "Song", "rec-1", null, [])])])
            };
            var evaluator = new ReleaseCandidateEvaluator(api);
            var evaluated = new HashSet<string>();
            var local = new LocalDisc();
            local.TrackNumbers.Add(1);

            var first = await evaluator.TryMatchAsync(release, [local], evaluated, CancellationToken.None);
            var second = await evaluator.TryMatchAsync(release, [local], evaluated, CancellationToken.None);

            Assert.NotNull(first);
            Assert.True(first.IsExact);
            Assert.Null(second);
            Assert.Equal(1, api.GetReleaseCalls);
        }

        [Fact]
        public async Task TryMatchAsync_ReturnsNullWhenLocalTrackIsNotCovered()
        {
            var release = CreateRelease("release-1");
            var api = new FakeApi
            {
                Release = new ParsedRelease(
                    release,
                    [new ReleaseMedia(1, "CD", [new AlbumTrack(1, "Song", "rec-1", null, [])])])
            };
            var evaluator = new ReleaseCandidateEvaluator(api);
            var local = new LocalDisc();
            local.TrackNumbers.Add(2);

            var match = await evaluator.TryMatchAsync(
                release,
                [local],
                new HashSet<string>(),
                CancellationToken.None);

            Assert.Null(match);
        }

        private static ReleaseSummary CreateRelease(string id)
        {
            return new ReleaseSummary(
                id,
                "Album",
                "2020-01-01",
                "Official",
                "US",
                null,
                null,
                null,
                "Album",
                "rg-1",
                [],
                []);
        }

        private sealed class FakeApi : IMusicBrainzApi
        {
            public ParsedRelease Release { get; set; } = new(
                CreateRelease("default"),
                []);

            public int GetReleaseCalls { get; private set; }

            public Task<ParsedRelease> GetReleaseAsync(string releaseMbid, CancellationToken ct)
            {
                GetReleaseCalls++;
                return Task.FromResult(Release);
            }

            public Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
                string album,
                string? artist,
                int limit,
                CancellationToken ct)
            {
                return Task.FromResult<IReadOnlyList<ScoredRelease>>([]);
            }

            public Task<ParsedReleaseGroup> GetReleaseGroupAsync(string rgMbid, CancellationToken ct)
            {
                return Task.FromResult(new ParsedReleaseGroup(
                    null,
                    null,
                    null,
                    null,
                    [],
                    []));
            }

            public void Dispose()
            {
            }
        }
    }
}
