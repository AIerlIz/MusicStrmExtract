namespace MusicStrmExtract.IntegrationTests;

/// <summary>Runs only when a live Emby endpoint and API key are explicitly configured.</summary>
internal sealed class EmbyFactAttribute : FactAttribute
{
    public EmbyFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EMBY_BASE_URL"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EMBY_API_KEY")))
        {
            Skip = "Set EMBY_BASE_URL and EMBY_API_KEY to run live Emby integration tests.";
        }
    }
}

/// <summary>Runs only when external MusicBrainz integration tests are explicitly enabled.</summary>
internal sealed class LiveMusicBrainzFactAttribute : FactAttribute
{
    public LiveMusicBrainzFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_MUSICBRAINZ_INTEGRATION_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set RUN_MUSICBRAINZ_INTEGRATION_TESTS=1 to run live MusicBrainz tests.";
        }
    }
}
