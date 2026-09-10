using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;

using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicStrmLocalProviderTests
    {
        [Fact]
        public async Task GetMetadata_NonStrmPath_ReturnsEmptyWithoutResolvingAlbum()
        {
            var resolver = new RecordingAlbumResolutionService();
            var provider = CreateProvider(resolver);

            var result = await provider.GetMetadata(
                CreateItemInfo(@"C:\music\Artist\Album\01 - Track.flac"),
                new LibraryOptions(),
                directoryService: null!,
                CancellationToken.None);

            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
            Assert.Equal(0, resolver.CallCount);
        }

        [Fact]
        public async Task GetMetadata_StrmPath_UsesResolverAndBuildsAudioMetadata()
        {
            using var fixture = new AlbumFixture();
            var resolver = new RecordingAlbumResolutionService
            {
                Result = BuildAlbumResult()
            };
            var provider = CreateProvider(resolver);

            var result = await provider.GetMetadata(
                CreateItemInfo(fixture.StrmPath),
                new LibraryOptions(),
                directoryService: null!,
                CancellationToken.None);

            Assert.True(result.HasMetadata);
            var item = Assert.IsType<MediaBrowser.Controller.Entities.Audio.Audio>(result.Item);
            Assert.Equal("Song 1", item.Name);
            Assert.Equal("Album", item.Album);
            Assert.Equal(1, item.IndexNumber);
            Assert.Equal("rec-1", item.ProviderIds[PluginConstants.MusicBrainzTrack]);
            Assert.Equal("release-1", item.ProviderIds[PluginConstants.MusicBrainzAlbum]);
            Assert.Equal("artist-1", item.ProviderIds[PluginConstants.MusicBrainzArtist]);
            Assert.Equal("artist-1", item.ProviderIds[PluginConstants.MusicBrainzAlbumArtist]);
            Assert.Equal("rg-1", item.ProviderIds[PluginConstants.MusicBrainzReleaseGroup]);

            var request = Assert.Single(resolver.Requests);
            Assert.Equal("Album", request.AlbumFolder);
            Assert.Equal("Artist", request.ArtistFolder);
            Assert.Equal(string.Empty, request.MusicBrainzBaseUrl);
            var disc = Assert.Single(request.LocalDiscs);
            Assert.Equal(new[] { 1 }, disc.TrackNumbers);
        }

        [Fact]
        public async Task GetMetadata_ResolverNetworkFailure_ReturnsEmpty()
        {
            using var fixture = new AlbumFixture();
            var resolver = new RecordingAlbumResolutionService
            {
                Exception = new HttpRequestException("MusicBrainz unavailable")
            };
            var provider = CreateProvider(resolver);

            var result = await provider.GetMetadata(
                CreateItemInfo(fixture.StrmPath),
                new LibraryOptions(),
                directoryService: null!,
                CancellationToken.None);

            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
            Assert.Equal(1, resolver.CallCount);
        }

        [Fact]
        public async Task GetMetadata_ResolverCancellation_IsPropagated()
        {
            using var fixture = new AlbumFixture();
            var resolver = new RecordingAlbumResolutionService();
            var provider = CreateProvider(resolver);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => provider.GetMetadata(
                    CreateItemInfo(fixture.StrmPath),
                    new LibraryOptions(),
                    directoryService: null!,
                    cts.Token));

            Assert.Equal(1, resolver.CallCount);
        }

        private static MusicStrmLocalProvider CreateProvider(IAlbumResolutionService resolver)
        {
            return new MusicStrmLocalProvider(
                CreateLogger(),
                new StubConfigurationSource(),
                resolver);
        }

        private static ItemInfo CreateItemInfo(string path)
        {
            var info = (ItemInfo)RuntimeHelpers.GetUninitializedObject(typeof(ItemInfo));
            info.Path = path;
            return info;
        }

        private static ILogger CreateLogger()
        {
            return DispatchProxy.Create<ILogger, NoOpLogger>();
        }

        private static AlbumSearchResult BuildAlbumResult()
        {
            var track = new AlbumTrack(
                1,
                "Song 1",
                "rec-1",
                "artist-1",
                ["Artist"]);
            var media = new ReleaseMedia(1, "CD", [track]);
            return new AlbumSearchResult(
                true,
                "Album",
                2020,
                "release-1",
                "rg-1",
                "Artist",
                "artist-1",
                [media]);
        }

        private sealed class StubConfigurationSource : IMusicStrmConfigurationSource
        {
            public PluginConfiguration Current { get; } = new();
        }

        private sealed class RecordingAlbumResolutionService : IAlbumResolutionService
        {
            public List<AlbumResolutionRequest> Requests { get; } = [];

            public AlbumSearchResult Result { get; set; } = AlbumSearchResult.Empty;

            public Exception? Exception { get; set; }

            public int CallCount => Requests.Count;

            public Task<AlbumSearchResult> ResolveAsync(
                AlbumResolutionRequest request,
                CancellationToken ct)
            {
                Requests.Add(request);
                ct.ThrowIfCancellationRequested();

                if (Exception is not null)
                    throw Exception;

                return Task.FromResult(Result);
            }
        }

        private sealed class AlbumFixture : IDisposable
        {
            public AlbumFixture()
            {
                Root = Path.Combine(
                    Path.GetTempPath(),
                    "MusicStrmExtract.Tests",
                    Guid.NewGuid().ToString("N"));
                var albumDirectory = Path.Combine(Root, "Artist", "Album");
                _ = Directory.CreateDirectory(albumDirectory);
                StrmPath = Path.Combine(albumDirectory, "01 - Song.strm");
                File.WriteAllText(StrmPath, "http://example.test/song");
            }

            public string Root { get; }

            public string StrmPath { get; }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
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
