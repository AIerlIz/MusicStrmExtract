using System;
using System.Collections.Generic;

using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class PluginCacheManagerTests
    {
        [Fact]
        public void Clear_RemovesAlbumAndDirectoryCaches()
        {
            var albumCache = new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 10);
            var scanCache = new TtlCache<AlbumDirectoryScan>(TimeSpan.FromMinutes(1), 10);
            albumCache.Set("album", AlbumSearchResult.Empty);
            scanCache.Set("scan", new AlbumDirectoryScan(
                [],
                new Dictionary<int, List<TrackReference>>()));
            var manager = new PluginCacheManager(albumCache, scanCache);

            Assert.Equal(1, manager.AlbumResolutionCount);
            Assert.Equal(1, manager.AlbumDirectoryScanCount);

            manager.Clear();

            Assert.Equal(0, manager.AlbumResolutionCount);
            Assert.Equal(0, manager.AlbumDirectoryScanCount);
        }
    }
}
