using System.Net.Http.Json;

using MusicStrmExtract;
using MusicStrmExtract.Online;

namespace MusicStrmExtract.IntegrationTests;

/// <summary>
/// End-to-end tests against a live Emby Server instance.
/// These tests are skipped when EMBY_BASE_URL is not configured.
/// </summary>
public class EmbyE2eTests
{
    private const string PluginGuid = "6a2f9c4e-8d3b-4f6a-9c1e-2b7d4a5f0e21";

    private static readonly string? EmbyBaseUrl = Environment.GetEnvironmentVariable("EMBY_BASE_URL");
    private static readonly string? EmbyApiKey = Environment.GetEnvironmentVariable("EMBY_API_KEY");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(EmbyBaseUrl!) };
        client.DefaultRequestHeaders.Add("X-Emby-Token", EmbyApiKey!);
        return client;
    }

    #region Plugin Health

    [EmbyFact]
    public async Task Plugin_Should_Be_Installed()
    {
        using var http = CreateClient();
        var plugins = await http.GetFromJsonAsync<PluginInfo[]>("Plugins")
            ?? throw new InvalidOperationException("No plugins returned");
        var plugin = plugins.FirstOrDefault(p => p.Id == PluginGuid)
            ?? throw new ArgumentException($"Plugin {PluginGuid} not found. Installed: {string.Join(", ", plugins.Select(p => p.Id))}");
        Assert.NotNull(plugin);
        Assert.Equal("Music Strm Extract", plugin.Name);
        Assert.NotNull(plugin.Version);
    }

    [EmbyFact]
    public async Task Plugin_Should_Have_Valid_Version()
    {
        using var http = CreateClient();
        var plugins = await http.GetFromJsonAsync<PluginInfo[]>("Plugins")
            ?? throw new InvalidOperationException("No plugins returned");
        var plugin = plugins.FirstOrDefault(p => p.Id == PluginGuid)
            ?? throw new ArgumentException("Plugin not installed");
        Assert.NotNull(plugin.Version);
        Assert.True(plugin.Version!.Major >= 1, "Plugin version should be >= 1.x");
    }

    #endregion

    #region Audio Item Verification

    [EmbyFact]
    public async Task Audio_Items_Should_Be_Indexed()
    {
        using var http = CreateClient();
        var result = await http.GetFromJsonAsync<EmbyPagedResult>(
            "Items?IncludeItemTypes=Audio&Recursive=true&Limit=1&TotalRecordCount=true")
            ?? throw new InvalidOperationException("Failed to fetch audio items");
        Assert.True(result.TotalRecordCount > 0, "No audio items found in the library");
    }

    [EmbyFact]
    public async Task Strm_Files_Should_Exist_In_Library()
    {
        using var http = CreateClient();
        var result = await http.GetFromJsonAsync<EmbyPagedResult>(
            "Items?IncludeItemTypes=Audio&Recursive=true&Limit=10&Fields=Path,Name")
            ?? throw new InvalidOperationException("Failed to fetch items");
        Assert.NotEmpty(result.Items);
        Assert.True(result.Items.Any(i => i.Path?.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) == true),
            "No .strm files found in the library");
    }

    #endregion

    #region Metadata Refresh

    [EmbyFact]
    public async Task Refresh_Single_Item_Should_Return_Success()
    {
        using var http = CreateClient();
        var result = await http.GetFromJsonAsync<EmbyPagedResult>(
            "Items?IncludeItemTypes=Audio&Recursive=true&Limit=1&Fields=Id")
            ?? throw new InvalidOperationException("Failed to fetch items");
        Assert.NotEmpty(result.Items);
        var response = await http.PostAsJsonAsync(
            $"Items/{result.Items[0].Id}/Refresh",
            new { MetadataRefreshMode = "FullRefresh" });
        Assert.True(response.IsSuccessStatusCode, $"Refresh failed with status: {response.StatusCode}");
    }

    [EmbyFact]
    public async Task Refresh_Strm_Item_Should_Succeed()
    {
        using var http = CreateClient();
        var r = await http.GetFromJsonAsync<EmbyPagedResult>("Items?IncludeItemTypes=Audio&Recursive=true&Limit=50&Fields=Path,Id"); var result = r ?? new EmbyPagedResult(0, []);
        var strmItem = result.Items.FirstOrDefault(i =>
            i.Path?.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) == true);
        if (strmItem is null)
            Assert.Fail("No .strm file found in library");
        var refreshResponse = await http.PostAsJsonAsync(
            $"Items/{strmItem.Id}/Refresh",
            new { MetadataRefreshMode = "FullRefresh" });
        Assert.True(refreshResponse.IsSuccessStatusCode,
            $"Refresh POST failed: {refreshResponse.StatusCode}");
        // Give Emby time to process the refresh
        await Task.Delay(3000);
    }

    #endregion

    #region Library Structure

    [EmbyFact]
    public async Task Music_Virtual_Folder_Should_Be_Configured()
    {
        using var http = CreateClient();
        var json = await http.GetStringAsync(new Uri(http.BaseAddress!, "Library/VirtualFolders?refreshLibrary=false"));
        var folders = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(json) ?? [];
        var musicFolders = folders.Where(f => f.GetProperty("CollectionType").GetString() == "music").ToArray();
        Assert.NotEmpty(musicFolders);
        Assert.False(string.IsNullOrWhiteSpace(musicFolders[0].GetProperty("Name").GetString()));
    }

    [EmbyFact]
    public async Task Music_Folder_Should_Have_At_Least_One_Path()
    {
        using var http = CreateClient();
        var json = await http.GetStringAsync(new Uri(http.BaseAddress!, "Library/VirtualFolders?refreshLibrary=false"));
        var folders = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(json) ?? [];
        var musicFolders = folders.Where(f => f.GetProperty("CollectionType").GetString() == "music").ToArray();
        if (musicFolders.Length == 0)
            Assert.Fail("No music virtual folder found");
        var locations = musicFolders[0].GetProperty("Locations");
        Assert.True(locations.GetArrayLength() > 0, "Music folder has no paths configured");
    }

    #endregion

    // ---- Helper types ----

    private record PluginInfo(string Id, string Name, Version? Version);
    private record EmbyPagedResult(int TotalRecordCount, List<AudioItem> Items);
    private record AudioItem(string Id, string? Path, string? Name);
}
