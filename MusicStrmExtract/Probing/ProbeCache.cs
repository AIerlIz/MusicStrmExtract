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
        private static readonly Dictionary<string, (DateTime MtimeUtc, ProbeResult? Result)> Cache =
            new Dictionary<string, (DateTime, ProbeResult?)>(StringComparer.OrdinalIgnoreCase);

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

            lock (Gate)
            {
                if (Cache.TryGetValue(strmPath, out var entry) && entry.MtimeUtc == mtime)
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

                Cache[strmPath] = (mtime, result);
            }

            return result;
        }
    }
}