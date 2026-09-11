using System;
using System.Collections.Generic;
using System.Linq;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ReleaseGroupScorerTests
    {
        /// <summary>构造一个含有多个版本的 release-group 候选列表。</summary>
        private static IReadOnlyList<ReleaseSummary> BuildRgJson(params (string Id, string Status, string? Barcode, string? Country,
            string? Date, bool HasCDDisc)[] releases)
        {
            var result = new List<ReleaseSummary>();
            foreach (var release in releases)
            {
                var (id, status, barcode, country, date, hasCDDisc) = release;
                var media = hasCDDisc
                    ? (IReadOnlyList<ReleaseMediaInfo>)new[] { new ReleaseMediaInfo(1, "CD", 13) }
                    : Array.Empty<ReleaseMediaInfo>();
                result.Add(new ReleaseSummary(
                    id,
                    null,
                    date,
                    status,
                    country,
                    barcode,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<ArtistCredit>(),
                    media));
            }

            return result;
        }

        [Fact]
        public void ScoreAll_Official_WithBarcode_Wins()
        {
            var rg = BuildRgJson(
                ("a", "Official", "123", "US", "2014-10-27", true),
                ("b", "Bootleg",  "123", "US", "2014-10-27", false),
                ("c", "Official", null,  "US", "2014-10-27", false)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal(3, scored.Count);
            Assert.Equal("a", scored[0].Release.Id);
            Assert.True(scored[0].Score > scored[1].Score);
            Assert.True(scored[0].Score > scored[2].Score);
        }

        [Fact]
        public void ScoreAll_Bootleg_ScoredLowest()
        {
            var rg = BuildRgJson(
                ("boot", "Bootleg", "123", "US", "2014-10-27", false),
                ("off",  "Official", null, "US", "2014-10-27", false)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("off", scored[0].Release.Id);
            Assert.Equal("boot", scored[1].Release.Id);
        }

        [Fact]
        public void ScoreAll_BarcodedHighFrequency_GetsBonus()
        {
            // barcode "ABC" 出现 3 次 → +min(3*5, 50) = +15
            var rg = BuildRgJson(
                ("a", "Official", "ABC", "US", "2014-10-27", true),
                ("b", "Official", "ABC", "GB", "2014-10-27", true),
                ("c", "Official", "ABC", "JP", "2014-10-27", true),
                ("d", "Official", "XYZ", "US", "2014-10-27", true)
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
                ("pseudo", "Pseudo-Release", null, null, null, false),
                ("official", "Official", "123", "US", "2014-01-01", true)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("official", scored[0].Release.Id);
            Assert.True(scored[0].Score > scored[1].Score);
        }

        [Fact]
        public void ScoreAll_CompleteDate_GetsBonus()
        {
            var rg = BuildRgJson(
                ("a", "Official", "123", "US", "2014-10-27", true),
                ("b", "Official", "123", "US", "2014", false)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("a", scored[0].Release.Id);
            Assert.True(scored[0].Score > scored[1].Score);
        }

        [Fact]
        public void ScoreAll_CdFormat_GetsBonus()
        {
            // 同样 Official+barcode+日期,CD 版应高于无 media 信息版
            var rg = BuildRgJson(
                ("cd",  "Official", "123", "US", "2014-10-27", true),
                ("nod", "Official", "123", "US", "2014-10-27", false)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg);
            Assert.Equal("cd", scored[0].Release.Id);
            Assert.True(scored[0].Score > scored[1].Score);
        }

        [Fact]
        public void ScoreAll_YearProximity_Tiebreak()
        {
            // 同分时本地年份 2004 应优先命中 2004 原版而非 2008 重版
            var rg = BuildRgJson(
                ("tw2008", "Official", "4547366035711", "TW", "2008-01-23", true),
                ("tw2004", "Official", "4716331042928", "TW", "2004-08-03", true),
                ("tw2020", "Official", "0194397682816", "TW", "2020-11-06", false)
            );
            // 无 localYear → 按分数降序（三者同分，排序不确定）
            var scoredNoYear = ReleaseGroupScorer.ScoreAll(rg);
            // 有 localYear=2004 → 2004 版排第一
            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2004);
            Assert.Equal("tw2004", scored[0].Release.Id);
        }

        [Fact]
        public void ScoreAll_YearProximity_EqualGap_EarlierDateWins()
        {
            // 年份差值相同时（如本地 2005，候选 2004 vs 2006），日期更早者优先（首发原版）
            var rg = BuildRgJson(
                ("late", "Official", "ABC", "US", "2006-03-15", true),
                ("early", "Official", "ABC", "US", "2004-08-03", true)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2005);
            Assert.Equal("early", scored[0].Release.Id);
        }

        [Fact]
        public void ScoreAll_YearProximity_EarlierDateWinsBeforeMinorQualityBonus()
        {
            // 2004 原版带歧义描述,2006 再版无歧义且完整日期齐全;
            // 年份贴近相同层内应仍先比日期,不能被质量分把再版抬到原版前。
            var rg = BuildRgJson(
                ("late", "Official", "ABC", "US", "2006-03-15", true),
                ("early", "Official", "ABC", "US", "2004-08-03", true)
            );

            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2005);

            Assert.Equal("early", scored[0].Release.Id);
        }

        [Fact]
        public void ScoreAll_YearProximity_NoYear_StillSortsByScore()
        {
            // localYear=null 时仅按分数排序，不做年份就近
            var rg = BuildRgJson(
                ("a", "Official", "123", "US", "2014-10-27", true),
                ("b", "Bootleg",  "123", "US", "2014-10-27", false)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: null);
            Assert.Equal("a", scored[0].Release.Id);
        }

        [Fact]
        public void InferPreferredCountry_ChoosesMostCommonCountry()
        {
            // US 出现 2 次最多,应被选为默认偏好国家
            var rg = BuildRgJson(
                ("us1", "Official", "ABC", "US", "2014-10-27", true),
                ("us2", "Official", "ABC", "US", "2014-10-27", true),
                ("jp1", "Official", "ABC", "JP", "2014-10-27", true),
                ("cl1", "Official", "ABC", "CL", "2014-10-27", true)
            );
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 13));
            Assert.Equal("US", ReleaseGroupScorer.InferPreferredCountry(rg, new[] { local }));
        }

        [Fact]
        public void InferPreferredCountry_IgnoresWithdrawnAndNoBarcode()
        {
            // CA 有 3 个候选,但都是 Withdrawn/无 barcode,应被排除;US 才是多数
            var rg = BuildRgJson(
                ("wd1", "Withdrawn", "XYZ", "CA", "2014-10-27", true),
                ("wd2", "Withdrawn", "XYZ", "CA", "2014-10-27", true),
                ("nb1", "Official", null, "CA", "2014-10-27", true),
                ("us1", "Official", "ABC", "US", "2014-10-27", true),
                ("us2", "Official", "ABC", "US", "2014-10-27", true)
            );
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 13));
            Assert.Equal("US", ReleaseGroupScorer.InferPreferredCountry(rg, new[] { local }));
        }

        [Fact]
        public void ScoreAll_PreferredCountry_BreaksTie()
        {
            // US/AR/CL 同分;指定偏好 US 后,US 应排到第一
            var rg = BuildRgJson(
                ("us", "Official", "602547071668", "US", "2014-10-27", true),
                ("ar", "Official", "602547071668", "AR", "2014-10-27", true),
                ("cl", "Official", "602547071668", "CL", "2014-10-27", true)
            );
            var withUs = ReleaseGroupScorer.ScoreAll(rg, preferredCountry: "US");
            Assert.Equal("us", withUs[0].Release.Id);
            Assert.True(withUs[0].Rank < withUs[1].Rank);
        }

        [Fact]
        public void ScoreAll_PreferredCountry_DoesNotOutrankOfficialInOtherCountry()
        {
            // 偏好国只有 Bootleg 时,country 权重不应把 Bootleg 抬到 Official 之上
            var rg = BuildRgJson(
                ("boot", "Bootleg", "ABC", "US", "2014-10-27", true),
                ("official", "Official", "ABC", "GB", "2014-10-27", true)
            );
            var scored = ReleaseGroupScorer.ScoreAll(rg, preferredCountry: "US");

            Assert.Equal("official", scored[0].Release.Id);
            Assert.True(scored[0].Score > scored[1].Score);
        }

        [Fact]
        public void ScoreAll_PreferredCountry_OutranksForeignOfficialWithHigherBarcodeFrequency()
        {
            // 同一实体版的高频条码覆盖多个国家,但 US 仍是多数国家;
            // 国家偏好应选 US 官方版,不能被国外高频条码的频次加分压过。
            var rg = BuildRgJson(
                ("ar", "Official", "COMMON", "AR", "2014-10-27", true),
                ("au", "Official", "COMMON", "AU", "2014-10-27", true),
                ("bg", "Official", "COMMON", "BG", "2014-10-27", true),
                ("cl", "Official", "COMMON", "CL", "2014-10-27", true),
                ("xe", "Official", "COMMON", "XE", "2014-10-27", true),
                ("us1", "Official", "843930013500", "US", "2014-10-27", true),
                ("us2", "Official", "843930013500", "US", "2014-10-27", true),
                ("us3", "Official", "843930013500", "US", "2014-10-27", true)
            );
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 13));

            var preferredCountry = ReleaseGroupScorer.InferPreferredCountry(rg, new[] { local });
            var scored = ReleaseGroupScorer.ScoreAll(rg, preferredCountry: preferredCountry);

            Assert.Equal("US", preferredCountry);
            Assert.Contains(scored[0].Release.Id, new[] { "us1", "us2", "us3" });
        }

        [Fact]
        public void InferPreferredCountry_ReturnsNull_WhenNoCompatible()
        {
            // 唯一候选无 barcode,不应产生偏好国家
            var rg = BuildRgJson(
                ("a", "Official", null, "US", "2014-10-27", true)
            );
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 13));
            Assert.Null(ReleaseGroupScorer.InferPreferredCountry(rg, new[] { local }));
        }

        [Fact]
        public void ScoreAll_CountryRankBase_StrictlyLessThanYearGapStep()
        {
            // 量级契约的数值级锁定:国家层权重必须严格小于年份层"最小步进"(相邻年份差 1 = YearGapRankBase)。
            // 构造偏好国(US)年份差 1 vs 非偏好国(JP)年份差 0 的相邻场景:
            //  - 契约成立(CountryRankBase < YearGapRankBase):JP 免国家惩罚,年份更近 → JP 胜出;
            //  - 契约被违反(CountryRankBase >= YearGapRankBase):US 的国家惩罚不足以抵消 1 个单位年份差上的优势,
            //    国家层翻越年份层 → US 胜出,本测试失败。
            // 这是 方案 B"数值即唯一控制点"能否拦住契约违规的关键测试。
            var rg = BuildRgJson(
                ("preferredFar", "Official", "ABC", "US", "2005-06-01", true), // 年份差 1
                ("foreignNear", "Official", "ABC", "JP", "2004-06-01", true)); // 年份差 0

            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2004, preferredCountry: "US");

            Assert.Equal("foreignNear", scored[0].Release.Id);
            Assert.True(scored[0].Rank < scored[1].Rank);
        }

        [Fact]
        public void ScoreAll_CountryLayer_DoesNotCrossYearGapLayer()
        {
            // 量级契约的行为化锁定:偏好国版本虽然免于国家惩罚,但年份差距更大时,
            // 仍应排在"年份更贴近的非偏好国官方版"之后 —— 国家层不得翻越年份贴近层。
            var rg = BuildRgJson(
                ("preferredFar", "Official", "ABC", "US", "2010-06-01", true),
                ("foreignNear", "Official", "ABC", "JP", "2005-06-01", true)
            );

            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2004, preferredCountry: "US");

            // US 版本年份差 6,JP 版本年份差 1;国家惩罚不足以让 US 翻越年份层。
            Assert.Equal("foreignNear", scored[0].Release.Id);
            Assert.True(scored[0].Rank < scored[1].Rank);
        }

        [Fact]
        public void ScoreAll_StatusLayer_DoesNotCrossYearGapLayer()
        {
            // 状态层的隔离锁定:Official 但年份差最远(9999) 必须仍排在
            // Bootleg 但年份差 0(最近) 之前 —— 锁定 StatusRankBase > YearGapRankBase × MissingYearDistance。
            // 当前该隔离完全没有测试,若有人调小 StatusRankBase 使年份层能翻越状态层,本测试失败。
            var rg = BuildRgJson(
                ("officialFar", "Official", "ABC", "US", null, true),   // 无日期 → 年份差 = MissingYearDistance(9999)
                ("bootlegNear", "Bootleg", "ABC", "US", "2004-08-03", true)); // 年份差 0(最贴近)

            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2004);

            Assert.Equal("officialFar", scored[0].Release.Id);
            Assert.True(scored[0].Rank < scored[1].Rank);
        }

        [Fact]
        public void ScoreAll_MultipleMissingDates_AllSortAfterRealDates()
        {
            // 缺失日期与真实日期的混合场景:两张缺失日期的候选应稳定排在真实日期之后,
            // 且彼此之间按 id 稳定(缺失日期统一归一到同一哨兵值)。
            var rg = BuildRgJson(
                ("dated", "Official", "ABC", "US", "2004-08-03", true),
                ("undatedB", "Official", "ABC", "US", null, true),
                ("undatedA", "Official", "ABC", "US", null, true)
            );

            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2004);

            Assert.Equal("dated", scored[0].Release.Id);
            Assert.Equal("undatedA", scored[1].Release.Id);
            Assert.Equal("undatedB", scored[2].Release.Id);
        }

        [Fact]
        public void ScoreAll_PreferredCountryAbsent_PreservesRelativeOrder()
        {
            // 惩罚式设计的核心保证:候选里没有偏好国时,所有官方版都被同等推后一步,
            // 因此相对顺序必须与"完全不启用国家层"时完全一致(惩罚是对称的,不会改变序)。
            // 这防止有人改成"给偏好国加分"——那样无匹配国家时层内退化为恒等,
            // 与不启用路径的行为假设分叉。
            var rg = BuildRgJson(
                ("jp", "Official", "ABC", "JP", "2004-08-03", true),
                ("gb", "Official", "ABC", "GB", "2005-08-03", true)
            );

            var withoutPreference = ReleaseGroupScorer.ScoreAll(rg, localYear: 2004);
            var withUnmatchedPreference = ReleaseGroupScorer.ScoreAll(
                rg,
                localYear: 2004,
                preferredCountry: "US");

            Assert.Equal(
                withoutPreference.Select(r => r.Release.Id),
                withUnmatchedPreference.Select(r => r.Release.Id));
        }

        [Fact]
        public void ScoreAll_PreferenceOnlyPenalizesOfficialReleases()
        {
            // 非官方版本不参与国家层:偏好国里的 Bootleg 不应因"恰好在偏好国"而获得相对优势,
            // 非偏好国的官方版也仍应按状态层稳定胜出。
            var rg = BuildRgJson(
                ("bootPreferred", "Bootleg", "ABC", "US", "2004-08-03", true),
                ("officialForeign", "Official", "ABC", "GB", "2004-08-03", true)
            );

            var scored = ReleaseGroupScorer.ScoreAll(rg, localYear: 2004, preferredCountry: "US");

            Assert.Equal("officialForeign", scored[0].Release.Id);
            Assert.True(scored[0].Rank < scored[1].Rank);
        }

        [Fact]
        public void InferPreferredCountry_IgnoresWorldwideDigitalReleases()
        {
            // XW 的 24bit/2024 数字版即使带 barcode 也不参与国家推断，
            // TW CD+VCD 应作为有效实体版进入偏好统计。
            var digital24 = BuildMediaRelease(
                "dig24",
                "XW",
                "00602458942408",
                "2005-11-11",
                new ReleaseMediaInfo(1, "Digital Media", 12));
            var digital2024 = BuildMediaRelease(
                "dig2024",
                "XW",
                "602458942392",
                "2024-01-05",
                new ReleaseMediaInfo(1, "Digital Media", 12));
            var tw = BuildMediaRelease(
                "tw",
                "TW",
                "828767594125",
                "2005-10-31",
                new ReleaseMediaInfo(1, "CD", 12),
                new ReleaseMediaInfo(2, "VCD", 3));

            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 12));

            var preferred = ReleaseGroupScorer.InferPreferredCountry(
                new[] { digital24, digital2024, tw },
                new[] { local });

            Assert.Equal("TW", preferred);
        }

        private static ReleaseSummary BuildMediaRelease(
            string id,
            string country,
            string barcode,
            string date,
            params ReleaseMediaInfo[] media)
        {
            return new ReleaseSummary(
                id,
                "11月的蕭邦",
                date,
                "Official",
                country,
                barcode,
                null,
                null,
                "Album",
                null,
                Array.Empty<ArtistCredit>(),
                media);
        }
    }
}