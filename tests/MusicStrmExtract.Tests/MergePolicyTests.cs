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
        public void Merge_Ambiguous_OverridesEmbedded_OnlineFirst()
        {
            var ambiguous = new OnlineMetadata { Kind = OnlineMatchKind.AmbiguousTextMatch, Source = "MusicBrainz", Note = "多候选" };
            ambiguous.Fields.Title = "其它歌";
            ambiguous.Fields.Album = "Wrong Album";

            var (final, applied, kind, _) = MergePolicy.Merge(Embedded(), ambiguous);

            Assert.True(applied);
            Assert.Equal(OnlineMatchKind.AmbiguousTextMatch, kind);
            Assert.Equal("Wrong Album", final.Album); // 在线优先:best 候选覆盖内嵌
            Assert.Equal("其它歌", final.Title);
        }

        [Fact]
        public void Merge_OnlineMissingFields_BackToEmbedded_ExceptMbid()
        {
            // 在线缺失的字段(如 MB 无年份)回填内嵌;但 MBID 不回填(防内嵌脏 ID 污染 ProviderIds)
            var embedded = Embedded();
            embedded.MusicBrainzTrackId = "t-embedded"; // 脏的内嵌 ID:在线命中时不得回填
            var online = new OnlineMetadata { Kind = OnlineMatchKind.UniqueTextMatch, Source = "MusicBrainz", Note = "文本唯一高置信" };
            online.Fields.Title = "Qi-Li-Xiang";
            online.Fields.Album = "Common Jasmine Orange";
            // 未设置 Year / MBID

            var (final, _, kind, _) = MergePolicy.Merge(embedded, online);

            Assert.Equal(OnlineMatchKind.UniqueTextMatch, kind);
            Assert.Equal("Qi-Li-Xiang", final.Title);     // 在线优先覆盖标题
            Assert.Equal(2004, final.Year);               // 在线缺失 -> 内嵌兜底
            Assert.Null(final.MusicBrainzTrackId);        // MBID 不回填
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
