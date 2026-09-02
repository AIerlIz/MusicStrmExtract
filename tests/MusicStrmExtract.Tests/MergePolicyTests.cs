using MusicStrmExtract.Metadata;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MergePolicyTests
    {
        private static TrackMetadata Embedded(string title = "七里香", string album = "Common Jasmine Orange (七里香)")
        {
            var md = new TrackMetadata { Title = title, Album = album };
            md.Artists.Add("Jay Chou (周杰倫)");
            md.AlbumArtists.Add("Jay Chou (周杰倫)");
            md.Year = 2004;
            return md;
        }

        private static OnlineMetadata OnlineExact(string title = "七里香", string album = "Common Jasmine Orange")
        {
            var online = new OnlineMetadata { Kind = OnlineMatchKind.ExactByMbid, Source = "MusicBrainz" };
            online.Fields.Title = title;
            online.Fields.Album = album;
            online.Fields.Artists.Add("Jay Chou");
            online.Fields.Year = 2004;
            online.ReleaseMbid = "r-111";
            return online;
        }

        [Fact]
        public void Merge_ExactByMbid_OverridesEmbedded()
        {
            var (final, applied, kind, _) = MergePolicy.Merge(Embedded(), OnlineExact());

            Assert.True(applied);
            Assert.Equal(OnlineMatchKind.ExactByMbid, kind);
            Assert.Equal("Common Jasmine Orange", final.Album); // 在线优先覆盖内嵌
            Assert.Equal("Jay Chou", final.Artists[0]);
        }

        [Fact]
        public void Merge_Ambiguous_KeepsEmbedded()
        {
            var ambiguous = new OnlineMetadata { Kind = OnlineMatchKind.AmbiguousTextMatch, Source = "MusicBrainz", Note = "多候选" };
            ambiguous.Fields.Title = "其它歌";
            ambiguous.Fields.Album = "Wrong Album";

            var (final, applied, kind, _) = MergePolicy.Merge(Embedded(), ambiguous);

            Assert.False(applied);
            Assert.Equal(OnlineMatchKind.AmbiguousTextMatch, kind);
            Assert.Equal("Common Jasmine Orange (七里香)", final.Album);
            Assert.Equal("七里香", final.Title);
        }

        [Fact]
        public void Merge_ITunesFallback_OnlyFillsMissingAndNeverTitle()
        {
            // 内嵌无专辑 -> iTunes 补专辑; 标题绝不能变成罗马字
            var embedded = new TrackMetadata { Title = "我的地盤" };
            embedded.Artists.Add("Jay Chou");
            var online = new OnlineMetadata { Kind = OnlineMatchKind.ITunesFallback, Source = "iTunes" };
            online.Fields.Album = "Common Jasmine Orange";
            online.Fields.Year = 2004;
            online.Fields.Title = "Wo De Di Pan"; // 罗马字(模拟 iTunes)

            var (final, _, kind, _) = MergePolicy.Merge(embedded, online);

            Assert.Equal(OnlineMatchKind.ITunesFallback, kind);
            Assert.Equal("我的地盤", final.Title);
            Assert.Equal("Common Jasmine Orange", final.Album);
            Assert.Equal(2004, final.Year);
        }

        [Fact]
        public void Merge_NoneOrNull_ReturnsEmbedded()
        {
            var embedded = Embedded();
            var (final, applied, kind, _) = MergePolicy.Merge(embedded, null);
            Assert.False(applied);
            Assert.Equal(OnlineMatchKind.None, kind);
            Assert.Equal(embedded, final);

            var none = new OnlineMetadata { Kind = OnlineMatchKind.None };
            var (final2, applied2, _, _) = MergePolicy.Merge(embedded, none);
            Assert.False(applied2);
            Assert.Equal(embedded, final2);
        }

        [Fact]
        public void NormalizeTitle_IgnoresParensAndCase()
        {
            Assert.Equal("commonjasmineorange", MergePolicy.NormalizeTitle("Common Jasmine Orange (七里香)"));
            Assert.Equal("qilixiang", MergePolicy.NormalizeTitle("Qi-Li-Xiang"));
            // 中文字符保留(同一语言匹配), 括号注释与标点剥离
            Assert.Equal("七里香qilixiang", MergePolicy.NormalizeTitle("七里香 Qi-Li-Xiang 【专辑】"));
        }
    }
}
