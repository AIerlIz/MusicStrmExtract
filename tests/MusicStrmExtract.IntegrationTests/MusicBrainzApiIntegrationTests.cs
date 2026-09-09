using MusicStrmExtract.Online;

namespace MusicStrmExtract.IntegrationTests;

/// <summary>
/// Integration tests for the MusicBrainz API layer against a live MusicBrainz server.
/// </summary>
public class MusicBrainzApiIntegrationTests
{
    private const string AlternativeBaseUrl = "https://musicbrainz.emby.tv";

    [Fact]
    public async Task SearchReleases_Against_Official_Server_Returns_Results()
    {
        var api = new MusicBrainzApi(null);
        try
        {
            var results = await api.SearchReleasesAsync("1989", "Taylor Swift", 5, CancellationToken.None);

            Assert.NotEmpty(results);
            Assert.True(results.All(r => r.Release.Id is not null), "All results should have an ID");
            Assert.True(results.All(r => r.Score > 0), "All results should have a positive score");
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task GetReleaseAsync_Parses_Tracklist_Correctly()
    {
        var api = new MusicBrainzApi(null);
        try
        {
            var release = await api.GetReleaseAsync("62b45cf8-1e4f-4f62-b221-b0c391823e52", CancellationToken.None);

            Assert.NotNull(release);
            Assert.Equal("62b45cf8-1e4f-4f62-b221-b0c391823e52", release.Release.Id);
            Assert.NotEmpty(release.Medias);
            Assert.True(release.Medias.Sum(m => m.Tracks.Count) > 0,
                "Release should have at least one track across all media");
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task GetReleaseGroupAsync_Parses_Group_With_Releases()
    {
        var api = new MusicBrainzApi(null);
        try
        {
            var group = await api.GetReleaseGroupAsync("1c4770b3-b7a3-4d44-a7a9-8e2dbb74b85a", CancellationToken.None);

            Assert.NotNull(group);
            Assert.Equal("1c4770b3-b7a3-4d44-a7a9-8e2dbb74b85a", group.Id);
            Assert.NotEmpty(group.Releases);
            Assert.True(group.Releases.Count > 0,
                "Release group should have at least one release");
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task SearchReleases_With_Alt_BaseUrl_Succeeds()
    {
        var api = new MusicBrainzApi(AlternativeBaseUrl);
        try
        {
            var results = await api.SearchReleasesAsync("叶惠美", "周杰伦", 3, CancellationToken.None);

            Assert.NotEmpty(results);
            Assert.True(results.Any(r => r.Release.Title?.Contains("叶惠美", StringComparison.OrdinalIgnoreCase) == true),
                "Should find 叶惠美 album");
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task RateLimiter_Holds_Exclusive_Access()
    {
        var gate = StaticMusicBrainzRateGate.Instance;
        var startTime = DateTime.UtcNow;

        using (await gate.AcquireAsync(CancellationToken.None))
        {
            await Task.Delay(100);
        }

        using (await gate.AcquireAsync(CancellationToken.None))
        {
            // Should have waited at least the minimum interval
        }

        var elapsed = DateTime.UtcNow - startTime;
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(1000),
            $"Expected at least 1000ms gap, got {elapsed.TotalMilliseconds}ms");
    }
}
