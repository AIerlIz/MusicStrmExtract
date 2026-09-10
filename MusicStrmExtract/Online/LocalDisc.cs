using System.Collections.ObjectModel;

namespace MusicStrmExtract.Online;

/// <summary>本地专辑目录中的一个碟组;DiscNumber 为空表示单碟/文件名未标碟。</summary>
public sealed class LocalDisc
{
    public int? DiscNumber { get; set; }

    public TrackNumberCollection TrackNumbers { get; } = [];
}

/// <summary>仅允许增量和批量添加的轨号集合,避免暴露 List 实现细节。</summary>
public sealed class TrackNumberCollection : Collection<int>
{
    public void AddRange(IEnumerable<int> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        foreach (var number in numbers)
            Add(number);
    }

    public void Sort()
    {
        var sorted = this.OrderBy(number => number).ToArray();
        Clear();
        AddRange(sorted);
    }
}
