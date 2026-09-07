using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;

using MusicStrmExtract.Ui;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class StaleMusicAlbumRepairServiceTests
    {
        [Fact]
        public void Run_DeletesOnlyStaleAlbum()
        {
            var context = new RepairContext();
            var stale = new MusicAlbum { Name = "Stale", Path = null };
            var withMbid = new MusicAlbum { Name = "Kept", Path = null };
            var withPath = new MusicAlbum { Name = "Physical", Path = @"C:\music\Album" };
            withMbid.ProviderIds[PluginConstants.MusicBrainzAlbum] = "release-1";
            context.Albums.Add(stale);
            context.Albums.Add(withMbid);
            context.Albums.Add(withPath);

            var result = CreateService(context).Run();

            Assert.Contains("已删除 1", result);
            Assert.Equal(new[] { stale }, context.Deleted);
        }

        [Fact]
        public void Run_AbortsWhenScanStartsAfterAlbumSnapshot()
        {
            var context = new RepairContext();
            context.Albums.Add(new MusicAlbum { Name = "Stale", Path = null });
            context.OnGetAlbums = () => context.IsScanRunning = true;

            var result = CreateService(context).Run();

            Assert.Contains("扫描已开始", result);
            Assert.Empty(context.Deleted);
        }

        [Fact]
        public void Run_ChecksCancellationInsideDeletionLoop()
        {
            var context = new RepairContext();
            context.Albums.Add(new MusicAlbum { Name = "A", Path = null });
            context.Albums.Add(new MusicAlbum { Name = "B", Path = null });
            var cts = new CancellationTokenSource();
            context.OnDelete = () => cts.Cancel();

            Assert.ThrowsAny<OperationCanceledException>(
                () => CreateService(context).Run(null, cts.Token));

            Assert.Single(context.Deleted);
        }

        [Fact]
        public void Run_QueuesStrmWithAlbumMbidAndMissingAlbumLink()
        {
            var context = new RepairContext();
            var audio = new Audio
            {
                Name = "Track",
                Path = @"C:\music\Album\01 - Track.flac.strm"
            };
            audio.ProviderIds[PluginConstants.MusicBrainzAlbum] = "release-1";
            context.Audios.Add(audio);

            var result = CreateService(context).Run();

            Assert.Contains("已排队刷新 1", result);
            var queued = Assert.Single(context.Queued);
            Assert.Equal(audio.InternalId, queued.Item1);
        }

        private static StaleMusicAlbumRepairService CreateService(RepairContext context)
        {
            var logger = DispatchProxy.Create<ILogger, NoOpLogger>();
            return new StaleMusicAlbumRepairService(
                logger,
                () => context.IsScanRunning,
                () => context.Audios,
                () =>
                {
                    context.OnGetAlbums?.Invoke();
                    return context.Albums;
                },
                album =>
                {
                    context.Deleted.Add(album);
                    context.OnDelete?.Invoke();
                },
                (id, options) => context.Queued.Add((id, options)));
        }

        private sealed class RepairContext
        {
            public List<Audio> Audios { get; } = new List<Audio>();

            public List<MusicAlbum> Albums { get; } = new List<MusicAlbum>();

            public List<MusicAlbum> Deleted { get; } = new List<MusicAlbum>();

            public List<(long Id, MetadataRefreshOptions Options)> Queued { get; } =
                new List<(long Id, MetadataRefreshOptions Options)>();

            public bool IsScanRunning { get; set; }

            public Action? OnDelete { get; set; }

            public Action? OnGetAlbums { get; set; }
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
