# Music Strm Extract

Emby Server 插件(.NET 8):为音乐库中的 `.strm` 音频条目补全元数据,解决 **Emby 不支持对 strm 音乐做刮削** 的问题。

## 问题根因

Emby 对音乐库中的 `.strm` 文件**不执行媒体探测** → 无法读取 strm 指向的 HTTP 直链音频文件的内嵌标签 → 条目只有裸文件名,没有标题/专辑/艺术家/MusicBrainz ID → 音乐库无法生成 MusicAlbum / MusicArtist 聚合,在线刮削器也无从匹配。

## 插件做什么

以**标准 Emby 元数据 Provider 架构**运行(本地读取器 + 在线下载器,写回/专辑组织/封面挂载全部交给 Emby 刷新引擎,插件自身不写库),两条路径:

1. **主路径·专辑轨道定位(零探测、整专辑一次)**:strm 文件名只解析数字轨号(`01 - 我的地盤.flac.strm` → 1)并扫描专辑文件夹得到本地轨号集合,不读取文件名标题;按"艺人文件夹 + 专辑文件夹名"作为查询词锁定 MusicBrainz release,用本地轨号覆盖**校验并选择 media(碟)**,随后单曲**按轨号直接取该专辑 tracklist 的 recording MBID/标题/艺人**——整条主路径不做本地文本比较、不做字形转换,不依赖内嵌标签,recording MBID 来自 MB 专辑、天然无脏 ID。
2. **降级路径·探测(原行为保底)**:结构不符(无 艺人/专辑 两层)、strm 文件名无轨号、专辑定位失败或轨号不在所选 tracklist 时——读取 strm 内容(URL,原样保留,含签名参数),用 ffprobe 远程探测(`-show_format -show_streams`)提取内嵌标签,再做逐曲 MusicBrainz 补全(标题搜索,候选宽松匹配);探测结果按 strm 文件修改时间缓存,重复扫描不重复探测。MB 不可达/无结果时保留内嵌字段(需自备 MB 连通,如镜像/代理)。

## 触发方式(无需计划任务、无需 PostScan)

- **入库/刷新即处理**:插件注册的 `ILocalMetadataProvider` 由 Emby 刷新引擎在**新 .strm 入库扫描、媒体库扫描、任何"刷新元数据"操作**时自动调用;专辑轨道定位(或降级探测) → 引擎持久化与专辑归组,全程标准流程,无自写库、无双写循环。整专辑定位结果 30 分钟缓存,同专辑重复刷新零额外 MusicBrainz 请求。
- 刷新引擎自带 remote 结果缓存,重复刷新不会重复请求 MusicBrainz。
- 无任何定时/计划任务。

## 实测结果(Emby 4.9.5.0;样本库:周杰伦《七里香》《叶惠美》,`艺人/专辑/NN - 标题.strm` 结构)

- 21 条 strm:文件系统结构规整率 **21/21**;文件名轨号可解析率 **21/21**(两专辑轨号 `1..N` 无缺无重)。
- **主路径专辑定位 21/21**:简体艺人/专辑文件夹名(`周杰伦`/`叶惠美`)→ MB release 查询直接命中官方繁体 release(artist `a223958d` 周杰倫);本地轨号覆盖校验通过后按轨号取 recording,每轨 MBID 真实可取;**全程零远程 ffprobe 探测、零逐曲文本搜索**。
- 多碟验证:两专辑的 MB 附加碟(MV,轨号未覆盖本地轨号集合)被正确排除,只取主 CD。
- 降级路径(原探测/逐曲)保留可用:结构或轨号不满足的条目自动回落;MB 不可达/无结果时保留内嵌字段。
- **循环根治**:旧版 Provider+PostScan 双写不一致导致的循环已随重构移除;写回与组织由 Emby 引擎单点完成。
- 专辑组织:写入由引擎统一持久化后,Emby 原生生成/刷新 `MusicAlbum` / `MusicArtist`;专辑 MBID 由插件直写(主路径)或 Emby 内置 MusicBrainz 抓取器(降级路径)在刷新时写入可达专辑。

## 构建

需要 .NET SDK(8.0+;本机用 10.0 交叉构建 net8.0 验证通过)。

```bash
dotnet build MusicStrmExtract/MusicStrmExtract.csproj -c Release
dotnet test tests/MusicStrmExtract.Tests/MusicStrmExtract.Tests.csproj   # 本机无 .NET 8 运行时需: DOTNET_ROLL_FORWARD=LatestMajor
```

产物:`MusicStrmExtract/bin/Release/` 下的 `MusicStrmExtract.dll`(无外部依赖)

## 部署

1. 复制 `MusicStrmExtract.dll` 到 Emby 的 `plugins` 目录:
   - Windows 便携版示例:`C:\Users\<user>\AppData\Roaming\Emby-Server\programdata\plugins\`
   - 其它安装(Docker/Linux)对应其 `plugins` 目录。
2. 重启 Emby Server。
3. 确认:插件在每次媒体库扫描后自动处理(无需操作);管理页 → 计划任务 → “Music Strm 元数据提取” 可手动运行。

## 配置

配置文件由 Emby 在**插件配置目录**生成/读取(注意:是 `plugins/configurations/`,不是 `config/plugins/`):

- Windows 便携版:`<data>\programdata\plugins\configurations\MusicStrmExtract.xml`
- 也可用 API:`POST /emby/Plugins/{pluginId}/Configuration`(body 为完整 JSON)。

| 项 | 默认 | 说明 |
|---|---|---|
| `FfprobePath` | 空 | ffprobe 完整路径;空则自动查找(Emby system 目录 → PATH) |
| `ExtraHeaders` | 空 | 每行 `Header: value` 的自定义 HTTP 头(防盗链 UA/Referer),传给 ffprobe |
| `ProbeTimeoutSeconds` | 30 | 单次远程探测超时(秒) |
| `EnableOnlineMetadata` | true | 在线补全(MusicBrainz)总开关 |
| `MusicBrainzBaseUrl` | 空 | MusicBrainz 端点;空=官方 `https://musicbrainz.org`(官方源在华语网络间歇 503),建议填稳定镜像如 `https://musicbrainz.emby.tv` |

## 已知限制

- **签名时效**:strm 直链若带时效签名(如示例的 `sign`),过期后探测将失败(本地 Provider 返回空,条目保持现状);重新生成 strm(刷新签名)后自动恢复。
- **MusicBrainz 可达性(需自备)**:在华语网络环境下 `musicbrainz.org` 可能繁忙/不可达;此时远程 Provider **不做在线补全**(保留内嵌字段,不降级其它数据源)。请自行保证 MB 连通(镜像 `musicbrainz.emby.tv` / 代理 / hosts),MB 恢复后 MBID 路径自动生效。
- 无 iTunes 兜底:在线元数据仅来自 MusicBrainz(用户约定);故 MB 不可达时专辑/封面等在线字段不会补全,条目保持内嵌字段。
- **封面**:由远程 Provider 的 `GetImages` 交给 Emby 引擎下载挂图(指向 Cover Art Archive,需 MB 连通);不再手写 `cover.jpg`。
- **专辑归属(AlbumId)为平台只读**:Emby 4.9 中 `Audio.AlbumId` 只读、音乐索引不对已关联条目重新归组。插件在线命中时会**直写真实条目的 `Album`/`AlbumArtists` 字符串**(保证 DB 一致,便于后续清理实体后重扫),但 API 显示与 MusicAlbum 组织仍由 Emby 内部的 `AlbumId` 实体关联决定;若要调整专辑归属,需在 Emby 侧删除对应 `MusicAlbum` 实体后重新扫描。
- strm URL 必须**原样**使用(示例中的签名尾 `:0` 去掉即 401)。

## 结构

```
MusicStrmExtract/
  Metadata/   TrackMetadata / TagParser(内嵌标签解析)/ MergePolicy(在线优先合并)/ OnlineMetadata
  Probing/    FfprobeRunner(定位+执行)/ ProbeResult(ffprobe JSON 解析)/ ProbeCache(mtime 缓存,免重复探测)
  Online/     MusicBrainzApi(限速/缓存/release+artist 查询与 tracklist 取回) / AlbumSearch(专辑锁定:艺人+专辑查询,轨号覆盖选碟) / OnlineResolver(降级路径逐曲编排,含熔断)
  Providers/  MusicStrmLocalProvider(ILocalMetadataProvider:主路径轨道定位 + 降级探测) / MusicStrmRemoteProvider(IRemoteMetadataProvider,MB 在线+封面)
tests/        xunit 单测(TagParser / ProbeResult / MergePolicy / AlbumSearch 轨道解析与选碟)
```
