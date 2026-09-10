using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class AlbumDirectoryScanServiceTests
    {
        [Fact]
        public void GetOrScan_CachesRepeatedScansForSameDirectory()
        {
            var calls = 0;
            var service = new AlbumDirectoryScanService(
                new TtlCache<AlbumDirectoryScan>(
                    System.TimeSpan.FromMinutes(1),
                    10),
                (path, _) =>
                {
                    calls++;
                    return CreateScan();
                });

            var first = service.GetOrScan(@"C:\music\Artist\Album");
            var second = service.GetOrScan(@"C:\music\Artist\Album");

            Assert.Same(first, second);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task GetOrScan_SingleFlightsConcurrentScansForSameDirectory()
        {
            var calls = 0;
            var service = new AlbumDirectoryScanService(
                new TtlCache<AlbumDirectoryScan>(
                    System.TimeSpan.FromMinutes(1),
                    10),
                (path, _) =>
                {
                    System.Threading.Interlocked.Increment(ref calls);
                    System.Threading.Thread.Sleep(50);
                    return CreateScan();
                });

            var scans = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(() => service.GetOrScan(@"C:\music\Artist\Album"))));

            Assert.Equal(1, calls);
            Assert.All(scans, scan => Assert.Same(scans[0], scan));
        }

        private static AlbumDirectoryScan CreateScan()
        {
            var disc = new LocalDisc();
            disc.TrackNumbers.Add(1);
            return new AlbumDirectoryScan(
                [disc],
                new Dictionary<int, List<TrackReference>>
                {
                    [0] = [new TrackReference(1, false)]
                });
        }
    }
}
