using System.Text.Json;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ReleaseGroupScorerTests
    {
        /// <summary>构造一个含有多个版本的 release-group JSON 数组(字段与 MB release-group?inc=releases+media 一致)。</summary>
        private static JsonElement BuildRgJson(params (string Id, string Status, string? Barcode, string? Country,
            string? Date, bool HasCDDisc, string? Disambiguation)[] releases)
        {
            var sb = new System.Text.StringBuilder("[");
            for (var i = 0; i < releases.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var (id, status, barcode, country, date, hasCDDisc, disambig) = releases[i];
                sb.Append('{');
                sb.Append($"\"id\":\"{id}\"");
                sb.Append($",\"status\":\"{status}\"");
                if (!string.IsNullOrEmpty(barcode)) sb.Append($",\"barcode\":\"{barcode}\"");
                else sb.Append(",\"barcode\":null");
                if (!string.IsNullOrEmpty(country)) sb.Append($",\"country\":\"{country}\"");
                else sb.Append(",\"country\":null");
                if (!string.IsNullOrEmpty(date)) sb.Append($",\"date\":\"{date}\"");
                else sb.Append(",\"date\":null");
                if (!string.IsNullOrEmpty(disambig)) sb.Append($",\"disambiguation\":\"{disambig}\"");
                else sb.Append(",\"disambiguation\":null");
                // media(MB 字段名,非 medium-list)
                sb.Append(",\"media\":[");
                if (hasCDDisc) sb.Append("{\"format\":\"CD\"}");
                sb.Append(']');
                sb.Append('}');
            }
            sb.Append(']');
            return System.Text.Json.JsonDocument.Parse(sb.ToString()).RootElement;
        }

        [Fact]
        public void ScoreAll_Official_WithBarcode_Wins()
        {
            var rg = BuildRgJson(
                ("a", "Official", "123", "US", "2014-10-27", true, null),
                ("b", "Bootleg",  "123", "US", "2014-10-27", false, null),
                ("c", "Official", null,  "US", "2014-10-27", false, null)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal(3, scored.Count);
            Assert.Equal("a", scored[0].Item.GetProperty("id").GetString());
            Assert.True(scored[0].Score > scored[1].Score);
            Assert.True(scored[0].Score > scored[2].Score);
        }

        [Fact]
        public void ScoreAll_Bootleg_ScoredLowest()
        {
            var rg = BuildRgJson(
                ("boot", "Bootleg", "123", "US", "2014-10-27", false, null),
                ("off",  "Official", null, "US", "2014-10-27", false, null)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("off", scored[0].Item.GetProperty("id").GetString());
            Assert.Equal("boot", scored[1].Item.GetProperty("id").GetString());
        }

        [Fact]
        public void ScoreAll_BarcodedHighFrequency_GetsBonus()
        {
            // barcode "ABC" 出现 3 次 → +min(3*5, 50) = +15
            var rg = BuildRgJson(
                ("a", "Official", "ABC", "US", "2014-10-27", true, null),
                ("b", "Official", "ABC", "GB", "2014-10-27", true, null),
                ("c", "Official", "ABC", "JP", "2014-10-27", true, null),
                ("d", "Official", "XYZ", "US", "2014-10-27", true, null)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            // a/b/c 应同分（同 barcode 频次相同），d 应最低（barcode 频次低）
            Assert.Equal(scored[0].Score, scored[1].Score);
            Assert.Equal(scored[1].Score, scored[2].Score);
            Assert.True(scored[2].Score > scored[3].Score);
        }

        [Fact]
        public void ScoreAll_PseudoRelease_NegativeScore()
        {
            var rg = BuildRgJson(
                ("pseudo", "Pseudo-Release", null, null, null, false, null),
                ("official", "Official", "123", "US", "2014-01-01", true, null)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("official", scored[0].Item.GetProperty("id").GetString());
            Assert.True(scored[0].Score > scored[1].Score);
        }

        [Fact]
        public void ScoreAll_CompleteDate_GetsBonus()
        {
            var rg = BuildRgJson(
                ("a", "Official", "123", "US", "2014-10-27", true, null),
                ("b", "Official", "123", "US", "2014", false, null)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("a", scored[0].Item.GetProperty("id").GetString());
            Assert.True(scored[0].Score > scored[1].Score);
        }

        [Fact]
        public void ScoreAll_CdFormat_GetsBonus()
        {
            // 同样 Official+barcode+日期,CD 版应高于无 media 信息版
            var rg = BuildRgJson(
                ("cd",  "Official", "123", "US", "2014-10-27", true, null),
                ("nod", "Official", "123", "US", "2014-10-27", false, null)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("cd", scored[0].Item.GetProperty("id").GetString());
            Assert.True(scored[0].Score > scored[1].Score);
        }

        [Fact]
        public void ScoreAll_DisambiguationPenalty()
        {
            // 带歧义描述(如打折版)应低于无歧义版本
            var rg = BuildRgJson(
                ("plain", "Official", "123", "US", "2014-10-27", true, null),
                ("disc",  "Official", "123", "US", "2014-10-27", true, "MOINS CHER")
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("plain", scored[0].Item.GetProperty("id").GetString());
            Assert.True(scored[0].Score > scored[1].Score);
        }

        [Fact]
        public void ScoreAll_YearProximity_Tiebreak()
        {
            // 同分时本地年份 2004 应优先命中 2004 原版而非 2008 重版
            var rg = BuildRgJson(
                ("tw2008", "Official", "4547366035711", "TW", "2008-01-23", true, null),
                ("tw2004", "Official", "4716331042928", "TW", "2004-08-03", true, null),
                ("tw2020", "Official", "0194397682816", "TW", "2020-11-06", false, null)
            );
            // 无 localYear → 按分数降序（三者同分，排序不确定）
            var scoredNoYear = ReleaseGroupScorer.ScoreAll(rg);
            // 有 localYear=2004 → 2004 版排第一
            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2004);
            Assert.Equal("tw2004", scored[0].Item.GetProperty("id").GetString());
        }

        [Fact]
        public void ScoreAll_YearProximity_EqualGap_EarlierDateWins()
        {
            // 年份差值相同时（如本地 2005，候选 2004 vs 2006），日期更早者优先（首发原版）
            var rg = BuildRgJson(
                ("late", "Official", "ABC", "US", "2006-03-15", true, null),
                ("early", "Official", "ABC", "US", "2004-08-03", true, null)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2005);
            Assert.Equal("early", scored[0].Item.GetProperty("id").GetString());
        }

        [Fact]
        public void ScoreAll_YearProximity_NoYear_StillSortsByScore()
        {
            // localYear=null 时仅按分数排序，不做年份就近
            var rg = BuildRgJson(
                ("a", "Official", "123", "US", "2014-10-27", true, null),
                ("b", "Bootleg",  "123", "US", "2014-10-27", false, null)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: null);
            Assert.Equal("a", scored[0].Item.GetProperty("id").GetString());
        }
    }
}