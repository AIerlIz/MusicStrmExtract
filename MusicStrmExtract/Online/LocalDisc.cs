using System.Collections.Generic;

namespace MusicStrmExtract.Online
{
    /// <summary>本地专辑目录中的一个碟组;DiscNumber 为空表示单碟/文件名未标碟。</summary>
    public sealed class LocalDisc
    {
        public int? DiscNumber { get; set; }

        public List<int> TrackNumbers { get; } = new List<int>();
    }
}
