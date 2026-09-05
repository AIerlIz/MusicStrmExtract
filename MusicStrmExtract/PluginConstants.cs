using System;

namespace MusicStrmExtract
{
    /// <summary>全插件共享的常量/标识。</summary>
    public static class PluginConstants
    {
        /// <summary>统一的 HTTP User-Agent;版本号随程序集版本(AssemblyVersion)自动同步,避免多处手写不一致。</summary>
        public static string UserAgent =>
            "MusicStrmExtract/" + (typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0")
            + " (Emby plugin; contact: local)";
    }
}
