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

        [Fact]
        public void Scan_UnauthorizedDirectory_DoesNotEscape()
        {
            // 权限异常(UnauthorizedAccessException)与 IOException 无继承关系,
            // 修复前会逃逸到 Emby 调用栈导致整库扫描中断;修复后应降级为 partial。
            // 仅在 Windows 上用 icacls 拒绝读取触发(其它平台直接跳过,避免误报)。
            if (!OperatingSystem.IsWindows())
                return;

            var albumDir = CreateTempAlbum();
            var denied = TryDenyReadAccess(albumDir);
            if (!denied)
                return; // 无法建立 ACL(权限不足等)时跳过,不产生假阳性

            try
            {
                string? warning = null;
                var scan = AlbumDirectoryScanner.Scan(albumDir, w => warning = w);

                Assert.Empty(scan.Discs);
                Assert.Empty(scan.RawTracks);
                Assert.NotNull(warning);
                Assert.Contains("result=partial", warning);
            }
            finally
            {
                TryRestoreAccess(albumDir);
                Directory.Delete(albumDir, recursive: true);
            }
        }

        /// <summary>用 icacls 拒绝 Everyone 读取目录;成功返回 true。仅 Windows 可用。</summary>
        private static bool TryDenyReadAccess(string dir)
        {
            var (exitCode, _) = RunProcess("icacls", $"\"{dir}\" /deny *S-1-1-0:(RX)");
            return exitCode == 0;
        }

        private static void TryRestoreAccess(string dir)
        {
            RunProcess("icacls", $"\"{dir}\" /remove:d *S-1-1-0");
        }

        private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
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
