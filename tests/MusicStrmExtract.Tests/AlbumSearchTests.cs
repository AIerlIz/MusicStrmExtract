using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class AlbumSearchTests
    {
        [Theory]
        [InlineData("叶惠美 (2003)", "叶惠美")]
        [InlineData("七里香 (2004)", "七里香")]
        [InlineData("七里香-2004", "七里香")]
        [InlineData("The Best of Jay Chou [2003]", "The Best of Jay Chou")]
        [InlineData("七里香", "七里香")]
        [InlineData(" 叶惠美 2003 ", "叶惠美")]
        [InlineData(" ", null)]
        // 版本标签不被剥离,仅年份被剥离
        [InlineData("1989 (Taylor's Version) (2023)", "1989 (Taylor's Version)")]
        [InlineData("Midnights (3am Edition) (2022)", "Midnights (3am Edition)")]
        [InlineData("1989 D.L.X.", "1989 D.L.X")]
        public void CleanAlbumName_StripsYearSuffix(string raw, string? expected)
        {
            Assert.Equal(expected, AlbumSearch.CleanAlbumName(raw));
        }

        [Fact]
        public void ParseReleaseMedias_ParsesTracksWithRecordingData()
        {
            var root = BuildRelease(
                (1, new[] { (1, "我的地盤"), (2, "七里香") }),
                (2, new[] { (1, "七里香MV") }));

            var medias = AlbumSearch.ParseReleaseMedias(root);

            Assert.Equal(2, medias.Count);
            Assert.Equal(1, medias[0].Position);
            Assert.Equal(2, medias[0].Tracks.Count);
            Assert.Equal(1, medias[0].Tracks[0].Number);
            Assert.Equal("我的地盤", medias[0].Tracks[0].Title);
            Assert.Equal("rec-1-1", medias[0].Tracks[0].RecordingMbid);
            Assert.Equal("周杰倫", medias[0].Tracks[0].Artists.First());
            Assert.Equal("art-1", medias[0].Tracks[0].ArtistMbid);
            Assert.Equal(2, medias[1].Position);
            Assert.Single(medias[1].Tracks);
        }

        [Fact]
        public void ParseCoverArtCount_PrefersFrontAndCounts()
        {
            var frontThree = BuildCoverArt(3, front: true);
            var noFrontFive = BuildCoverArt(5, front: false);
            var singleFront = BuildCoverArt(1, front: true);

            Assert.Equal(10003, AlbumSearch.ParseCoverArtCount(frontThree)); // 有正面 + 3 图
            Assert.Equal(5, AlbumSearch.ParseCoverArtCount(noFrontFive));    // 无正面 + 5 图
            Assert.Equal(10001, AlbumSearch.ParseCoverArtCount(singleFront)); // 有正面 + 1 图
        }

        [Fact]
        public void SelectBestMedia_PicksMainCd_RejectsMvDisc()
        {
            var local = Local(1, 10);
            var root = BuildRelease(
                (1, Tracks(1, 10)),
                (2, new[] { (1, "七里香MV"), (2, "止戰之殤MV") }));

            var chosen = AlbumSearch.SelectBestMedia(local, AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(chosen);
            Assert.Equal(1, chosen!.Position); // MV 附加碟未覆盖本地轨号,不选中
        }

        [Fact]
        public void SelectBestMedia_AcceptsLocalSubsetOfMediaWithExtraTracks()
        {
            // 本地为普通版 10 轨,MB 为含 2 轨 bonus 的 12 轨版本 → 本地轨号全部存在即可选中
            var local = Local(1, 10);
            var tracks = Tracks(1, 10).Concat(new[] { (11, "Bonus A"), (12, "Bonus B") }).ToArray();
            var root = BuildRelease((1, tracks));

            var chosen = AlbumSearch.SelectBestMedia(local, AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(chosen);
            Assert.Equal(12, chosen!.Tracks.Count);
        }

        [Fact]
        public void SelectBestMedia_RejectsWhenMediaNumbersDoNotCoverLocal()
        {
            var local = Local(1, 10);
            var root = BuildRelease((1, Tracks(11, 10))); // 另一版本/精选,轨号与本地不重合

            var chosen = AlbumSearch.SelectBestMedia(local, AlbumSearch.ParseReleaseMedias(root));

            Assert.Null(chosen);
        }

        [Fact]
        public void SelectBestMedia_SelectsByNumbersOnly_WhenFileNamesHaveNoTitles()
        {
            var local = Enumerable.Range(1, 10)
                .Select(n => n)
                .ToList();

            var chosen = AlbumSearch.SelectBestMedia(local, AlbumSearch.ParseReleaseMedias(BuildRelease((1, Tracks(1, 10)))));

            Assert.NotNull(chosen);
        }

        [Fact]
        public void SelectBestMedia_AlignsByTrackNumber_WhenFirstTrackMissing()
        {
            var local = Enumerable.Range(2, 9)
                .Select(n => n)
                .ToList();
            var root = BuildRelease((1, Tracks(1, 10)));

            var chosen = AlbumSearch.SelectBestMedia(local, AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(chosen);
            Assert.Equal(1, chosen!.Position);
        }

        [Fact]
        public void SelectBestMedia_AlignsByTrackNumber_WhenMiddleTrackMissing()
        {
            var local = Enumerable.Range(1, 10)
                .Where(n => n != 5)
                .Select(n => n)
                .ToList();
            var root = BuildRelease((1, Tracks(1, 10)));

            var chosen = AlbumSearch.SelectBestMedia(local, AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(chosen);
            Assert.Equal(1, chosen!.Position);
        }

        [Fact]
        public void SelectBestMedia_TieBreaksToLowerMediaPosition()
        {
            // 两个 media 的轨号都覆盖本地(罕见):取 Position 较小者
            var local = Local(1, 3);
            var root = BuildRelease(
                (1, Tracks(1, 3)),
                (2, Tracks(1, 3)));

            var chosen = AlbumSearch.SelectBestMedia(local, AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(chosen);
            Assert.Equal(1, chosen!.Position);
        }

        [Fact]
        public void MapLocalDiscsToMedias_MapsExplicitDiscToMediaPosition()
        {
            var disc1 = LocalDisc(1, 1, 2);
            var disc2 = LocalDisc(2, 1);
            var disc3 = LocalDisc(3, 1);
            var root = BuildRelease(
                (1, new[] { (1, "Lavender Haze"), (2, "Maroon") }),
                (2, new[] { (1, "You're Losing Me") }),
                (3, new[] { (1, "Hits Different") }));

            var map = AlbumSearch.MapLocalDiscsToMedias(
                new[] { disc1, disc2, disc3 },
                AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.Equal(1, map![disc1].Position);
            Assert.Equal(2, map[disc2].Position);
            Assert.Equal(3, map[disc3].Position);
        }

        [Fact]
        public void MapLocalDiscsToMedias_RejectsWhenDiscPositionMissing()
        {
            var locals = new[] { LocalDisc(1, 1, 2), LocalDisc(2, 1) };
            var root = BuildRelease((1, new[] { (1, "A"), (2, "B") }));

            var map = AlbumSearch.MapLocalDiscsToMedias(locals, AlbumSearch.ParseReleaseMedias(root));

            Assert.Null(map);
        }

        [Fact]
        public void MapLocalDiscsToMedias_RejectsWhenMediaDoesNotCoverTrackNumbers()
        {
            var locals = new[] { LocalDisc(1, 1, 2, 3) };
            var root = BuildRelease((1, new[] { (1, "A"), (2, "B") }));

            var map = AlbumSearch.MapLocalDiscsToMedias(locals, AlbumSearch.ParseReleaseMedias(root));

            Assert.Null(map);
        }

        [Fact]
        public void MapLocalDiscsToMedias_RejectsDuplicateDiscPosition()
        {
            var locals = new[] { LocalDisc(1, 1), LocalDisc(1, 2) };
            var root = BuildRelease((1, new[] { (1, "A"), (2, "B") }));

            var map = AlbumSearch.MapLocalDiscsToMedias(locals, AlbumSearch.ParseReleaseMedias(root));

            Assert.Null(map);
        }

        [Fact]
        public void MapLocalDiscsToMedias_ImplicitGroupUsesLowestCoveringMedia()
        {
            var local = LocalDisc(null, 1, 2, 3);
            var root = BuildRelease(
                (1, new[] { (4, "D"), (5, "E") }),
                (2, new[] { (1, "A"), (2, "B"), (3, "C") }));

            var map = AlbumSearch.MapLocalDiscsToMedias(new[] { local }, AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.Equal(2, map![local].Position);
        }

        [Fact]
        public void HasExactTrackCount_TrueWhenEveryMediaMatchesLocalCount()
        {
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 10));
            var root = BuildRelease((1, Tracks(1, 10)));

            var map = AlbumSearch.MapLocalDiscsToMedias(
                new[] { local },
                AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.True(AlbumSearch.HasExactTrackCount(new[] { local }, map!));
        }

        [Fact]
        public void HasExactTrackCount_FalseWhenMediaHasBonusTracks()
        {
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 10));
            var bonusTracks = Tracks(1, 10)
                .Concat(new[] { (11, "Bonus A"), (12, "Bonus B") })
                .ToArray();
            var root = BuildRelease((1, bonusTracks));

            var map = AlbumSearch.MapLocalDiscsToMedias(
                new[] { local },
                AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.False(AlbumSearch.HasExactTrackCount(new[] { local }, map!));
        }

        [Fact]
        public void HasExactTrackCount_MatchesPerDiscForMultiDiscAlbums()
        {
            var disc1 = new LocalDisc();
            disc1.TrackNumbers.AddRange(Enumerable.Range(1, 17));
            var disc2 = new LocalDisc();
            disc2.TrackNumbers.AddRange(Enumerable.Range(1, 13));
            var root = BuildRelease(
                (1, Tracks(1, 17)),
                (2, Tracks(1, 13)));

            var map = AlbumSearch.MapLocalDiscsToMedias(
                new[] { disc1, disc2 },
                AlbumSearch.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.True(AlbumSearch.HasExactTrackCount(new[] { disc1, disc2 }, map!));
        }

        // ===== 测试数据构造 =====

        private static List<int> Local(int fromNumber, int count)
        {
            return Enumerable.Range(fromNumber, count)
                .Select(n => n)
                .ToList();
        }

        private static LocalDisc LocalDisc(int? discNumber, params int[] tracks)
        {
            var disc = new LocalDisc { DiscNumber = discNumber };
            disc.TrackNumbers.AddRange(tracks);
            return disc;
        }

        private static (int, string)[] Tracks(int fromNumber, int count)
        {
            return Enumerable.Range(fromNumber, count).Select(n => (n, $"歌{n}")).ToArray();
        }

        private static JsonElement BuildRelease(params (int Position, (int Number, string Title)[] Tracks)[] medias)
        {
            var sb = new System.Text.StringBuilder("{\"media\":[");
            for (var m = 0; m < medias.Length; m++)
            {
                if (m > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"position\":").Append(medias[m].Position).Append(",\"tracks\":[");
                for (var i = 0; i < medias[m].Tracks.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    var (num, title) = medias[m].Tracks[i];
                    sb.Append("{\"number\":\"").Append(num)
                      .Append("\",\"title\":\"").Append(title)
                      .Append("\",\"length\":200000,\"recording\":{\"id\":\"rec-")
                      .Append(medias[m].Position).Append('-').Append(num)
                      .Append("\",\"title\":\"").Append(title)
                      .Append("\",\"artist-credit\":[{\"artist\":{\"id\":\"art-1\",\"name\":\"周杰倫\"}}]}}");
                }

                sb.Append("]}");
            }

            sb.Append("]}");
            return JsonDocument.Parse(sb.ToString()).RootElement;
        }

        private static JsonElement BuildCoverArt(int count, bool front)
        {
            var sb = new System.Text.StringBuilder("{\"images\":[");
            for (var i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(front && i == 0 ? "{\"front\":true}" : "{\"front\":false}");
            }

            sb.Append("]}");
            return JsonDocument.Parse(sb.ToString()).RootElement;
        }
    }
}
