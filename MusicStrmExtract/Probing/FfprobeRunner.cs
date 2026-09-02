using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Metadata;

namespace MusicStrmExtract.Probing
{
    /// <summary>
    /// 定位并执行 ffprobe,对 strm 内的 HTTP(S) 目标做远程媒体探测。
    /// </summary>
    public sealed class FfprobeRunner
    {
        private readonly string _ffprobePath;
        private readonly int _timeoutSeconds;
        private readonly IReadOnlyList<string> _extraHeaders;

        public FfprobeRunner(string ffprobePath, int timeoutSeconds, string? extraHeaders)
        {
            _ffprobePath = ffprobePath;
            _timeoutSeconds = Math.Max(5, timeoutSeconds);
            _extraHeaders = ParseHeaders(extraHeaders);
        }

        /// <summary>依次尝试: 配置路径 → Emby 运行目录(system/)ffprobe(.exe) → PATH。</summary>
        public static string? Locate(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                foreach (var candidate in new[] { "ffprobe.exe", "ffprobe" })
                {
                    var full = Path.Combine(baseDir, candidate);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
            }

            // PATH 查找
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    foreach (var candidate in new[] { "ffprobe.exe", "ffprobe" })
                    {
                        var full = Path.Combine(dir.Trim(), candidate);
                        if (File.Exists(full))
                        {
                            return full;
                        }
                    }
                }
                catch (Exception)
                {
                    // 忽略不可访问的 PATH 目录
                }
            }

            return null;
        }

        /// <summary>探测 url,返回结构化结果;进程异常/超时/JSON 解析失败时返回 null(细节在 stderr/异常中)。</summary>
        public async Task<ProbeResult?> ProbeAsync(string url, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // 超时以微秒传给 ffprobe 网络层(-rw_timeout),覆盖 TCP 读/写等待
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-rw_timeout");
            psi.ArgumentList.Add((_timeoutSeconds * 1_000_000L).ToString());
            psi.ArgumentList.Add("-user_agent");
            psi.ArgumentList.Add("MusicStrmExtract/1.0");

            if (_extraHeaders.Count > 0)
            {
                var joined = string.Join("\r\n", _extraHeaders) + "\r\n";
                psi.ArgumentList.Add("-headers");
                psi.ArgumentList.Add(joined);
            }

            psi.ArgumentList.Add("-print_format");
            psi.ArgumentList.Add("json");
            psi.ArgumentList.Add("-show_format");
            psi.ArgumentList.Add("-show_streams");
            psi.ArgumentList.Add(url);

            try
            {
                using var process = new Process { StartInfo = psi };
                if (!process.Start())
                {
                    return null;
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds + 10));

                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                var stdout = await stdoutTask.ConfigureAwait(false);
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                {
                    var stderr = await stderrTask.ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"ffprobe exit={process.ExitCode}: {Truncate(stderr, 500)}");
                }

                return ProbeResult.FromJson(stdout);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"ffprobe 探测失败: {ex.Message}", ex);
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, max) + "...";
        }

        private static List<string> ParseHeaders(string? extraHeaders)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(extraHeaders))
            {
                return result;
            }

            foreach (var rawLine in extraHeaders.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Contains(':') && !line.StartsWith("#", StringComparison.Ordinal))
                {
                    result.Add(line);
                }
            }

            return result;
        }
    }
}
