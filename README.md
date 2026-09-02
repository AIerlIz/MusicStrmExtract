# Music Strm Extract

Emby Server 插件(.NET 8):为音乐库中的 `.strm` 音频条目补全元数据,解决 **Emby 不支持对 strm 音乐做刮削** 的问题。

## 问题根因

Emby 对音乐库中的 `.strm` 文件**不执行媒体探测** → 无法读取 strm 指向的 HTTP 直链音频文件的内嵌标签 → 条目只有裸文件名,没有标题/专辑/艺术家/MusicBrainz ID → 音乐库无法生成 MusicAlbum / MusicArtist 聚合,在线刮削器也无从匹配。

## 插件做什么

对音乐库(`CollectionType == music`)中每个 `.strm` 支撑的 Audio 条目:

1. **探测**:读取 strm 内容(URL,原样保留,含签名参数),用 ffprobe 远程探测(`-show_format -show_streams`),提取内嵌标签(flac Vorbis / mp3 ID3 / m4a MP4,大小写不敏感、别名兼容)。
2. **在线补全**(可关闭):MusicBrainz 优先 —— 内嵌带可信 MBID 则按 ID 精确取回;无 MBID 或 MBID 与标题不符则按 标题+艺术家 文本搜索;MusicBrainz 不可用/无结果时降级 **iTunes Search**(仅补专辑名/年份/封面,绝不覆盖中文标题——iTunes 返回罗马音译标题)。
3. **合并**:可信命中(MBID 精确 / 文本唯一高置信)→ 在线字段覆盖内嵌;模糊命中(多候选)→ 保留内嵌并记日志;在线无果 → 内嵌兜底。
4. **写回**:更新 Audio 条目(Name/Album/Artists/AlbumArtists/年份/曲号/碟号/流派/ProviderIds),并下载在线封面到 strm 所在专辑目录 `cover.jpg`。
5. **促成组织**:有变更时自动排队库扫描,让 Emby 生成 MusicAlbum / MusicArtist 并挂载专辑封面。
6. **专辑补写**:库状态稳定后,把同专辑音轨收集到的 MusicBrainz Album / ReleaseGroup MBID 与缺失年份补写到 MusicAlbum 条目,并对补到 MBID 的专辑触发一次刷新(让 Emby MusicBrainz 抓取器取回完整专辑详情)。

## 触发方式(不需要计划任务)

- **自动**:插件注册 `ILibraryPostScanTask` —— 每当 Emby 完成一次媒体库扫描(含新 strm 入库触发的自动扫描)即自动处理,无需任何手动/定时操作。
- **手动**:管理页 → 计划任务 → “Music Strm 元数据提取” 仍保留,可随时手动运行;默认不再注册定时触发器(避免与自动触发重复),如需要定时可在计划任务页自行添加。

## 实测结果(Emby 4.9.5.0)

- 21 条 strm(周杰伦《七里香》《叶惠美》):探测 21/21,写回变更 21/21,封面落盘 21,失败 0;重复运行写回变更 = 0(幂等)。
- **自动触发**:触发一次媒体库扫描后,插件(ILibraryPostScanTask)自动执行完整处理,无需计划任务;汇总 "写回变更=0, 专辑补写=…, 失败=0",无副作用、无死循环。
- 专辑组织:Emby 自动生成 2 个 `MusicAlbum` + 1 个 `MusicArtist` 并挂载 600×600 封面;MusicAlbum 年份齐全。专辑 MBID(MusicBrainzAlbum/ReleaseGroup/AudioDb)由 Emby 内置 MusicBrainz 抓取器(经 musicbrainz.emby.tv 镜像)在刷新时写入可达专辑;本插件 AlbumUpdater 会把音轨侧已有的 Album/ReleaseGroup MBID 汇总补写到专辑条目并对补到的专辑触发一次刷新。若 MusicBrainz 与 iTunes 均无专辑 MBID 源(如叶惠美镜像无匹配),专辑无 MBID 可写属数据源限制。

## 构建

需要 .NET SDK(8.0+;本机用 10.0 交叉构建 net8.0 验证通过)。

```bash
dotnet build MusicStrmExtract/MusicStrmExtract.csproj -c Release
dotnet test tests/MusicStrmExtract.Tests/MusicStrmExtract.Tests.csproj   # 本机无 .NET 8 运行时需: DOTNET_ROLL_FORWARD=LatestMajor
```

产物:`MusicStrmExtract/bin/Release/MusicStrmExtract.dll`

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
| `EnableOnlineMetadata` | true | 在线补全(MusicBrainz → iTunes)总开关 |
| `RequireExactOnlineMatch` | true | 模糊命中不覆盖内嵌(保底防误配) |
| `WriteAlbumCover` | true | 下载在线封面为专辑目录 `cover.jpg`(已有则跳过) |
| `MusicLibrariesOnly` | true | 仅处理 `CollectionType==music` 的库 |
| `WriteBack` | true | 是否写回 Emby 条目 |
| `MaxItemsToProcess` | 0 | 调试用:单次最多处理条数,0=不限 |
| `LogProbeDetails` | false | 调试:输出每条原始标签 |

## 已知限制

- **MusicBrainz 可达性**:在华语网络环境下 `musicbrainz.org` 可能繁忙/不可达;插件会自动熔断并降级 iTunes(实测全部走 iTunes 兜底成功)。MB 恢复后优先 MBID 路径自动生效。
- **iTunes 仅补专辑侧**:其 `trackName` 为罗马音译,设计上**不覆盖**内嵌中文标题;若需要纯英文标题可自行修改 `MergePolicy`。
- **内嵌封面兜底**:当前封面来源为在线封面(MB Cover Art Archive / iTunes);若在线无封面而目标内嵌封面,尚未提取(计划增强)。
- strm URL 必须**原样**使用(示例中的签名尾 `:0` 去掉即 401)。

## 结构

```
MusicStrmExtract/
  Metadata/   TrackMetadata / TagParser(内嵌标签解析)/ MergePolicy(分档合并)/ OnlineMetadata
  Probing/    FfprobeRunner(定位+执行)/ ProbeResult(ffprobe JSON 解析)
  Online/     MusicBrainzApi / ITunesApi / OnlineResolver(MBID→文本→iTunes 编排,含限速与熔断)
  Writing/    ItemWriter(写回字段/ProviderIds/封面下载)
  Tasks/      MusicStrmScanTask(计划任务,可手动) / MusicStrmPostScanTask(库扫描后自动, ILibraryPostScanTask)
  Processing/ MusicStrmProcessor(核心管线,单飞防并发)/ AlbumUpdater(补 MusicAlbum 条目)
tests/        xunit 单测(TagParser / ProbeResult / MergePolicy)
```
