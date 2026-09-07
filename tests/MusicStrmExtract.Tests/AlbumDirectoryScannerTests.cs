using System;
using System.IO;
using System.Linq;

using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class AlbumDirectoryScannerTests
    {
        [Fact]
        public void Scan_AlbumRoot_CollectsSingleDiscTrackNumbers()
        {
            var albumDir = CreateTempAlbum();
            try
            {
                CreateFiles(albumDir, "01 - A.flac.strm", "02 - B.m4a.strm", "03 - C.flac.strm");

                var scan = AlbumDirectoryScanner.Scan(albumDir);

                var disc = Assert.Single(scan.Discs);
                Assert.Null(disc.DiscNumber);
                Assert.Equal(new[] { 1, 2, 3 }, disc.TrackNumbers);
                Assert.Equal(3, scan.RawTracks[0].Count);
            }
            finally
            {
                Directory.Delete(albumDir, recursive: true);
            }
        }

        [Fact]
        public void Scan_DiscFolders_GroupByDiscNumber()
        {
            var albumDir = CreateTempAlbum();
            try
            {
                CreateFiles(albumDir, "disc 1/01 - A.flac.strm", "disc 1/02 - B.flac.strm", "CD2/01 - C.flac.strm");

                var scan = AlbumDirectoryScanner.Scan(albumDir);

                Assert.Equal(2, scan.Discs.Count);
                Assert.Equal(1, scan.Discs[0].DiscNumber);
                Assert.Equal(new[] { 1, 2 }, scan.Discs[0].TrackNumbers);
                Assert.Equal(2, scan.Discs[1].DiscNumber);
                Assert.Equal(new[] { 1 }, scan.Discs[1].TrackNumbers);
                Assert.Equal(2, scan.RawTracks[1].Count);
                Assert.Single(scan.RawTracks[2]);
            }
            finally
            {
                Directory.Delete(albumDir, recursive: true);
            }
        }

        [Fact]
        public void Scan_InterleavedCommentary_NormalizesTrackNumbers()
        {
            var albumDir = CreateTempAlbum();
            try
            {
                CreateFiles(
                    albumDir,
                    "01 - C1 (Commentary).flac.strm",
                    "02 - A.flac.strm",
                    "03 - C2 (Commentary).flac.strm",
                    "04 - B.flac.strm");

                var scan = AlbumDirectoryScanner.Scan(albumDir);

                var disc = Assert.Single(scan.Discs);
                Assert.Equal(new[] { 1, 2 }, disc.TrackNumbers);
                Assert.Equal(2, scan.RawTracks[0].Count(r => r.IsCommentary));
            }
            finally
            {
                Directory.Delete(albumDir, recursive: true);
            }
        }

        private static string CreateTempAlbum()
        {
            var albumDir = Path.Combine(Path.GetTempPath(), "MusicStrmExtract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(albumDir);
            return albumDir;
        }

        private static void CreateFiles(string albumDir, params string[] relativePaths)
        {
            foreach (var relative in relativePaths)
            {
                var path = Path.Combine(albumDir, relative);
                var parent = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(parent);
                File.WriteAllText(path, string.Empty);
            }
        }
    }
}
