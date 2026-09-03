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
            Assert.Equal(200, medias[0].Tracks[0].LengthSeconds); // 200000ms → 200s
            Assert.Equal("周杰倫", medias[0].Tracks[0].Artists.First());
            Assert.Equal("art-1", medias[0].Tracks[0].ArtistMbid);
            Assert.Equal(2, medias[1].Position);
            Assert.Single(medias[1].Tracks);
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
                .Select(n => new LocalTrack { Number = n })
                .ToList();

            var chosen = AlbumSearch.SelectBestMedia(local, AlbumSearch.ParseReleaseMedias(BuildRelease((1, Tracks(1, 10)))));

            Assert.NotNull(chosen);
        }

        [Fact]
        public void SelectBestMedia_AlignsByTrackNumber_WhenFirstTrackMissing()
        {
            var local = Enumerable.Range(2, 9)
                .Select(n => new LocalTrack { Number = n })
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
                .Select(n => new LocalTrack { Number = n })
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

        // ===== 测试数据构造 =====

        private static List<LocalTrack> Local(int fromNumber, int count)
        {
            return Enumerable.Range(fromNumber, count)
                .Select(n => new LocalTrack { Number = n })
                .ToList();
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
    }
}
