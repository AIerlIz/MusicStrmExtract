using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class CancellationTests
    {
        [Fact]
        public async Task AlbumSearch_RgLookupCancellation_IsPropagated()
        {
            var cts = new CancellationTokenSource();
            var api = new CancelOnRgLookupApi(cts);
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(new[] { 1, 2, 3 });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await new AlbumSearch(api, new NoCoverClient()).SearchForTrackMapAsync(
                    "Album",
                    "Artist",
                    new[] { local },
                    cts.Token);
            });
        }

        private sealed class NoCoverClient : ICoverArtClient
        {
            public Task<int> GetCoverArtCountAsync(string releaseMbid, CancellationToken ct)
                => Task.FromResult(0);
        }

        private sealed class CancelOnRgLookupApi : IMusicBrainzApi
        {
            private readonly CancellationTokenSource _cts;

            public CancelOnRgLookupApi(CancellationTokenSource cts)
            {
                _cts = cts;
            }

            public Task<JsonElement> SearchReleasesAsync(string album, string? artist, int limit, CancellationToken ct)
            {
                _cts.Cancel();
                return Task.FromResult(JsonDocument.Parse(
                    "{\"releases\":[{\"id\":\"release-1\",\"score\":100,\"title\":\"Album\"," +
                    "\"date\":\"2000-01-01\",\"status\":\"Official\",\"artist-credit\":[]," +
                    "\"release-group\":{\"id\":\"rg-1\"}}]}").RootElement);
            }

            public Task<JsonElement> GetReleaseGroupReleasesAsync(string rgMbid, CancellationToken ct)
            {
                _cts.Token.ThrowIfCancellationRequested();
                return Task.FromResult(JsonDocument.Parse("{}").RootElement);
            }

            public Task<JsonElement> GetReleaseAsync(string releaseMbid, CancellationToken ct)
                => Task.FromResult(JsonDocument.Parse("{}").RootElement);

            public void Dispose()
            {
            }
        }
    }
}
