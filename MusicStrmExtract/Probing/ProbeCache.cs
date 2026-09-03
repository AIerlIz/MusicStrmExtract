using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Probing
{
    /// <summary>
    /// 探测结果进程内缓存:按 strm 文件路径 + 最后修改时间缓存 ProbeResult。
    /// 标准 Provider 架构下每次库扫描/刷新都会调用本地 Provider,文件未变时直接复用缓存,避免重复远程探测。
    /// </summary>
    internal static class ProbeCache
    {
        /// <summary>缓存 TTL:远程目标内容可能变化而不改变 strm 文件 mtime,超时后即使 mtime 相同也强制重新探测。</summary>
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

        private static readonly Dictionary<string, (DateTime MtimeUtc, DateTime CreatedUtc, ProbeResult? Result)> Cache =
            new Dictionary<string, (DateTime, DateTime, ProbeResult?)>(StringComparer.OrdinalIgnoreCase);

        private static readonly object Gate = new object();

        public static async Task<ProbeResult?> ProbeAsync(
            FfprobeRunner runner,
            string strmPath,
            string url,
            CancellationToken ct)
        {
            DateTime mtime;
            try
            {
                mtime = File.GetLastWriteTimeUtc(strmPath);
            }
            catch (Exception)
            {
                mtime = DateTime.MinValue;
            }

            var now = DateTime.UtcNow;
            lock (Gate)
            {
                if (Cache.TryGetValue(strmPath, out var entry)
                    && entry.MtimeUtc == mtime
                    && now - entry.CreatedUtc < CacheTtl)
                {
                    return entry.Result;
                }
            }

            var result = await runner.ProbeAsync(url, ct).ConfigureAwait(false);

            lock (Gate)
            {
                if (Cache.Count > 1000)
                {
                    Cache.Clear();
                }

                Cache[strmPath] = (mtime, now, result);
            }

            return result;
        }
    }
}