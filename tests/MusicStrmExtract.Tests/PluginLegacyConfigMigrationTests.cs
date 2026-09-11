using System;
using System.IO;

using MusicStrmExtract;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class PluginLegacyConfigMigrationTests
    {
        [Fact]
        public void MigrateLegacyXmlConfiguration_MigratesBaseUrl()
        {
            var dir = CreateTempDir();
            var xmlPath = Path.Combine(dir, "MusicStrmExtract.xml");
            var jsonPath = Path.Combine(dir, "MusicStrmExtract.json");
            File.WriteAllText(xmlPath, "<Configuration><MusicBrainzBaseUrl>https://mirror.example</MusicBrainzBaseUrl></Configuration>");

            try
            {
                PluginConfiguration? saved = null;
                Plugin.MigrateLegacyXmlConfiguration(jsonPath, xmlPath, c => saved = c);

                Assert.NotNull(saved);
                Assert.Equal("https://mirror.example", saved!.MusicBrainzBaseUrl);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void MigrateLegacyXmlConfiguration_ExistingJson_SkipsMigration()
        {
            var dir = CreateTempDir();
            var xmlPath = Path.Combine(dir, "MusicStrmExtract.xml");
            var jsonPath = Path.Combine(dir, "MusicStrmExtract.json");
            File.WriteAllText(xmlPath, "<Configuration><MusicBrainzBaseUrl>x</MusicBrainzBaseUrl></Configuration>");
            File.WriteAllText(jsonPath, "{}");

            try
            {
                var savedCalled = false;
                Plugin.MigrateLegacyXmlConfiguration(jsonPath, xmlPath, _ => savedCalled = true);

                Assert.False(savedCalled);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void MigrateLegacyXmlConfiguration_MalformedXml_DoesNotEscape()
        {
            var dir = CreateTempDir();
            var xmlPath = Path.Combine(dir, "MusicStrmExtract.xml");
            var jsonPath = Path.Combine(dir, "MusicStrmExtract.json");
            File.WriteAllText(xmlPath, "<Configuration><Broken>");

            try
            {
                Plugin.MigrateLegacyXmlConfiguration(jsonPath, xmlPath, _ => throw new InvalidOperationException("不应被调用"));

                // 未抛异常即通过:MalformedXml(XmlException)被吞掉,迁移失败不阻塞启动
                Assert.True(true);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void MigrateLegacyXmlConfiguration_UnauthorizedXml_DoesNotEscape()
        {
            // UnauthorizedAccessException 与 IOException 无继承关系:旧实现只捕获 XmlException/IOException,
            // 配置目录不可读时会逃逸导致插件加载失败。修复后应与 M3 一致降级为吞掉。
            if (!OperatingSystem.IsWindows())
                return;

            var dir = CreateTempDir();
            var xmlPath = Path.Combine(dir, "MusicStrmExtract.xml");
            var jsonPath = Path.Combine(dir, "MusicStrmExtract.json");
            File.WriteAllText(xmlPath, "<Configuration><MusicBrainzBaseUrl>x</MusicBrainzBaseUrl></Configuration>");

            if (!TryDenyReadAccess(xmlPath))
            {
                Directory.Delete(dir, recursive: true);
                return; // 无法建立 ACL 时跳过,不产生假阳性
            }

            try
            {
                Plugin.MigrateLegacyXmlConfiguration(jsonPath, xmlPath, _ => throw new InvalidOperationException("不应被调用"));

                // 未抛异常即通过:权限异常被吞掉,不阻塞插件加载
                Assert.True(true);
            }
            finally
            {
                TryRestoreAccess(xmlPath);
                Directory.Delete(dir, recursive: true);
            }
        }

        private static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "MusicStrmExtract-plugin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static bool TryDenyReadAccess(string file)
        {
            var (exitCode, _) = RunProcess("icacls", $"\"{file}\" /deny *S-1-1-0:(R)");
            return exitCode == 0;
        }

        private static void TryRestoreAccess(string file)
        {
            RunProcess("icacls", $"\"{file}\" /remove:d *S-1-1-0");
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
    }
}
