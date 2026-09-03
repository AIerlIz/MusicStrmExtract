using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MusicStrmExtract.Metadata
{
    /// <summary>常用繁体→简体映射(覆盖音乐/艺人场景常见字,用于 MB 匹配归一)。</summary>
    public static class HanSimplifier
    {
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
            ['溝'] = '沟', ['煙'] = '烟', ['聲'] = '声', ['書'] = '书'
        };

        /// <summary>把字符串中的繁体字替换为简体;其余字符原样保留。</summary>
        public static string Simplify(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                sb.Append(Map.TryGetValue(c, out var simple) ? simple : c);
            }

            return sb.ToString();
        }
    }
}