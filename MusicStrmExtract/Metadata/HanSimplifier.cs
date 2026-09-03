using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MusicStrmExtract.Metadata
{
    /// <summary>
    /// 简繁归一:优先 OpenCC 词级转换(全覆盖、词义级),失败/词典缺失时回退内置常用字表。
    /// OpenCC.NET 的 Dictionary/JiebaResource 由 NuGet 输出到程序目录,首次使用惰性初始化。
    /// </summary>
    public static class HanSimplifier
    {
        private static readonly object InitGate = new object();
        private static bool _initialized;
        private static bool _openccAvailable;

        /// <summary>OpenCC 词级转换是否可用(词典已部署且初始化成功)。</summary>
        public static bool IsOpenCcAvailable
        {
            get
            {
                EnsureInitialized();
                return _openccAvailable;
            }
        }

        /// <summary>把字符串中的繁体/异体字转换为简体;未映射字符原样保留。</summary>
        public static string Simplify(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            EnsureInitialized();
            if (_openccAvailable)
            {
                try
                {
                    return OpenCCNET.ZhConverter.HantToHans(value!);
                }
                catch (Exception)
                {
                    // 词典缺失/转换异常:回退内置常用字表
                    _openccAvailable = false;
                }
            }

            return ApplyManualMap(value!);
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (InitGate)
            {
                if (_initialized)
                {
                    return;
                }

                try
                {
                    var baseDir = Path.GetDirectoryName(typeof(HanSimplifier).Assembly.Location) ?? AppContext.BaseDirectory;
                    var dictionary = Path.Combine(baseDir, "Dictionary");
                    var jieba = Path.Combine(baseDir, "JiebaResource");
                    if (Directory.Exists(dictionary) && Directory.Exists(jieba))
                    {
                        OpenCCNET.ZhConverter.Initialize(dictionary, jieba, false, OpenCCNET.SegmentMode.Jieba);
                        _openccAvailable = true;
                    }
                }
                catch (Exception)
                {
                    _openccAvailable = false;
                }

                _initialized = true;
            }
        }

        private static string ApplyManualMap(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                sb.Append(Map.TryGetValue(c, out var simple) ? simple : c);
            }

            return sb.ToString();
        }

        private static readonly Dictionary<char, char> Map = new Dictionary<char, char>
        {
            ['葉'] = '叶', ['裏'] = '里', ['裡'] = '里', ['倫'] = '伦', ['傑'] = '杰',
            ['盤'] = '盘', ['殤'] = '殇', ['種'] = '种', ['調'] = '调', ['園'] = '园',
            ['遊'] = '游', ['會'] = '会', ['鬥'] = '斗', ['亂'] = '乱', ['單'] = '单',
            ['雙'] = '双', ['懸'] = '悬', ['聽'] = '听', ['臺'] = '台', ['灣'] = '湾',
            ['見'] = '见', ['讓'] = '让', ['說'] = '说', ['話'] = '话', ['寫'] = '写',
            ['樂'] = '乐', ['國'] = '国', ['學'] = '学', ['寶'] = '宝', ['貝'] = '贝',
            ['歡'] = '欢', ['愛'] = '爱', ['勝'] = '胜', ['戰'] = '战', ['夢'] = '梦',
            ['舊'] = '旧', ['門'] = '门', ['問'] = '问', ['關'] = '关', ['開'] = '开',
            ['來'] = '来', ['東'] = '东', ['車'] = '车', ['發'] = '发', ['風'] = '风',
            ['蘭'] = '兰', ['驚'] = '惊', ['憶'] = '忆', ['淒'] = '凄', ['絕'] = '绝',
            ['無'] = '无', ['與'] = '与', ['雲'] = '云', ['飛'] = '飞',
            ['溝'] = '沟', ['煙'] = '烟', ['聲'] = '声', ['書'] = '书',
            ['適'] = '适', ['對'] = '对', ['態'] = '态', ['節'] = '节', ['網'] = '网',
            ['線'] = '线', ['縣'] = '县', ['總'] = '总', ['繼'] = '继', ['續'] = '续',
            ['紅'] = '红', ['經'] = '经', ['結'] = '结', ['繪'] = '绘', ['編'] = '编',
            ['縮'] = '缩', ['績'] = '绩', ['縱'] = '纵', ['緻'] = '致', ['維'] = '维',
            ['緒'] = '绪', ['緊'] = '紧', ['羅'] = '罗', ['罰'] = '罚', ['聯'] = '联',
            ['肅'] = '肃', ['臨'] = '临', ['舉'] = '举', ['華'] = '华', ['蘇'] = '苏',
            ['處'] = '处', ['虛'] = '虚', ['號'] = '号', ['虧'] = '亏', ['衝'] = '冲',
            ['裝'] = '装', ['雖'] = '虽', ['證'] = '证', ['讀'] = '读', ['變'] = '变',
            ['議'] = '议', ['護'] = '护', ['豔'] = '艳', ['購'] = '购', ['轉'] = '转',
            ['輝'] = '辉', ['輸'] = '输', ['農'] = '农', ['遲'] = '迟', ['遺'] = '遗',
            ['鄰'] = '邻', ['醫'] = '医', ['釋'] = '释', ['鐘'] = '钟', ['鐵'] = '铁',
            ['間'] = '间', ['闊'] = '阔', ['閃'] = '闪', ['閣'] = '阁', ['際'] = '际',
            ['隨'] = '随', ['隻'] = '只', ['難'] = '难', ['靜'] = '静', ['們'] = '们',
            ['偽'] = '伪', ['價'] = '价', ['險'] = '险', ['驗'] = '验', ['驅'] = '驱',
            ['體'] = '体', ['髮'] = '发', ['鬆'] = '松', ['頻'] = '频', ['頁'] = '页',
            ['項'] = '项', ['頂'] = '顶', ['頑'] = '顽', ['順'] = '顺', ['預'] = '预',
            ['觀'] = '观', ['覺'] = '觉', ['親'] = '亲', ['規'] = '规', ['視'] = '视',
            ['誰'] = '谁', ['遠'] = '远', ['燈'] = '灯', ['簡'] = '简', ['筆'] = '笔',
            ['統'] = '统', ['應'] = '应', ['務'] = '务', ['權'] = '权', ['隊'] = '队',
            ['陣'] = '阵', ['陸'] = '陆', ['陳'] = '陈', ['陽'] = '阳', ['陰'] = '阴',
            ['買'] = '买', ['賣'] = '卖', ['貴'] = '贵', ['賞'] = '赏', ['賜'] = '赐',
            ['賀'] = '贺', ['賦'] = '赋', ['贈'] = '赠', ['這'] = '这', ['過'] = '过',
            ['還'] = '还', ['選'] = '选', ['進'] = '进', ['週'] = '周', ['計'] = '计',
            ['記'] = '记', ['設'] = '设', ['評'] = '评', ['試'] = '试', ['詩'] = '诗',
            ['詞'] = '词', ['詳'] = '详', ['認'] = '认', ['誠'] = '诚', ['談'] = '谈',
            ['論'] = '论', ['講'] = '讲', ['謝'] = '谢', ['識'] = '识', ['譜'] = '谱',
            ['譯'] = '译', ['擇'] = '择'
        };

        }
    }