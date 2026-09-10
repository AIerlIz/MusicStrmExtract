using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Model.Logging;

using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;
using MusicStrmExtract.Ui;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicStrmPageViewTests
    {
        [Fact]
        public async Task RunCommand_ClearCacheUpdatesCacheAndDiagnosticsLabels()
        {
            var albumCache = new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 10);
            var scanCache = new TtlCache<AlbumDirectoryScan>(TimeSpan.FromMinutes(1), 10);
            albumCache.Set("album", AlbumSearchResult.Empty);
            scanCache.Set("scan", new AlbumDirectoryScan(
                [],
                new Dictionary<int, List<TrackReference>>()));
            var cacheManager = new PluginCacheManager(albumCache, scanCache);
            var diagnostics = new ResolutionDiagnosticsStore();
            diagnostics.Record(
                new AlbumResolutionRequest("Album", "Artist", [], string.Empty),
                AlbumResolutionOutcome.NotFound);
            var view = CreateView(cacheManager, diagnostics);

            _ = await view.RunCommand(
                string.Empty,
                MusicStrmPageOptions.ClearCacheCommand,
                string.Empty);

            Assert.Equal(0, cacheManager.AlbumResolutionCount);
            Assert.Equal(0, cacheManager.AlbumDirectoryScanCount);
            Assert.Contains("0 条", view.ContentData.CacheStatusLabel.Text, StringComparison.Ordinal);
            Assert.Contains("未命中 1", view.ContentData.DiagnosticsLabel.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RunCommand_RefreshDiagnosticsUpdatesLabel()
        {
            var cacheManager = new PluginCacheManager(
                new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 10),
                new TtlCache<AlbumDirectoryScan>(TimeSpan.FromMinutes(1), 10));
            var diagnostics = new ResolutionDiagnosticsStore();
            diagnostics.Record(
                new AlbumResolutionRequest("Album", "Artist", [], string.Empty),
                AlbumResolutionOutcome.NotFound);
            var view = CreateView(cacheManager, diagnostics);
            view.ContentData.DiagnosticsLabel.Text = "stale";

            _ = await view.RunCommand(
                string.Empty,
                MusicStrmPageOptions.RefreshDiagnosticsCommand,
                string.Empty);

            Assert.Contains("未命中 1", view.ContentData.DiagnosticsLabel.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RunCommand_CheckSourceUpdatesStatusLabel()
        {
            var cacheManager = new PluginCacheManager(
                new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 10),
                new TtlCache<AlbumDirectoryScan>(TimeSpan.FromMinutes(1), 10));
            var sourceCheckService = new MusicBrainzSourceCheckService(_ => new EmptySourceApi());
            var view = CreateView(
                cacheManager,
                new ResolutionDiagnosticsStore(),
                sourceCheckService);

            _ = await view.RunCommand(
                string.Empty,
                MusicStrmPageOptions.CheckSourceCommand,
                string.Empty);

            await WaitUntilContainsAsync(
                () => view.ContentData.SourceStatusLabel.Text,
                "连接成功",
                TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task RunCommand_PageSavePersistsEditedMusicBrainzBaseUrl()
        {
            var cacheManager = new PluginCacheManager(
                new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 10),
                new TtlCache<AlbumDirectoryScan>(TimeSpan.FromMinutes(1), 10));
            PluginConfiguration? saved = null;
            var view = CreateView(
                cacheManager,
                new ResolutionDiagnosticsStore(),
                loadOptions: () => new PluginConfiguration
                {
                    MusicBrainzBaseUrl = "https://old.example"
                },
                saveOptions: options => saved = options);
            view.ContentData.MusicBrainzBaseUrl = "https://musicbrainz.emby.tv";

            _ = await view.RunCommand(string.Empty, "PageSave", string.Empty);

            Assert.NotNull(saved);
            Assert.Equal("https://musicbrainz.emby.tv", saved.MusicBrainzBaseUrl);
        }

        private static MusicStrmPageView CreateView(
            PluginCacheManager cacheManager,
            ResolutionDiagnosticsStore diagnostics,
            MusicBrainzSourceCheckService? sourceCheckService = null,
            Func<PluginConfiguration>? loadOptions = null,
            Action<PluginConfiguration>? saveOptions = null)
        {
            var repairService = new LegacyAlbumRepairService(
                CreateLogger(),
                () => false,
                () => [],
                () => [],
                _ => { },
                (_, _) => { });
            sourceCheckService ??= new MusicBrainzSourceCheckService(_ => new UnusedApi());
            loadOptions ??= () => new PluginConfiguration();
            saveOptions ??= _ => { };

            return new MusicStrmPageView(
                "plugin",
                new MusicStrmPageOptions(),
                loadOptions,
                saveOptions,
                repairService,
                cacheManager,
                diagnostics,
                sourceCheckService);
        }

        private static async Task WaitUntilContainsAsync(
            Func<string> valueFactory,
            string expected,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (valueFactory().Contains(expected, StringComparison.Ordinal))
                    return;

                await Task.Delay(20);
            }

            Assert.Contains(expected, valueFactory(), StringComparison.Ordinal);
        }

        private static ILogger CreateLogger()
        {
            return DispatchProxy.Create<ILogger, NoOpLogger>();
        }

        private sealed class UnusedApi : IMusicBrainzApi
        {
            public Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
                string album,
                string? artist,
                int limit,
                CancellationToken ct)
            {
                throw new NotSupportedException();
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

        private sealed class EmptySourceApi : IMusicBrainzApi
        {
            public Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
                string album,
                string? artist,
                int limit,
                CancellationToken ct)
            {
                return Task.FromResult<IReadOnlyList<ScoredRelease>>([]);
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
