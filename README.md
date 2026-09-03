# Music Strm Extract

Emby Server 插件(.NET 8):为音乐库中的 `.strm` 音频条目补全元数据,解决 **Emby 不支持对 strm 音乐做刮削** 的问题。

## 问题根因

Emby 对音乐库中的 `.strm` 文件**不执行媒体探测** → 无法读取 strm 指向的 HTTP 直链音频文件的内嵌标签 → 条目只有裸文件名,没有标题/专辑/艺术家/MusicBrainz ID → 音乐库无法生成 MusicAlbum / MusicArtist 聚合,在线刮削器也无从匹配。

## 插件做什么

以**标准 Emby 元数据 Provider 架构**运行(本地读取器 + 在线下载器,写回/专辑组织/封面挂载全部交给 Emby 刷新引擎,插件自身不写库):

1. **本地读取器 `ILocalMetadataProvider<Audio>`(探测)**:Emby 在扫描/刷新每个 Audio 条目时调用——读取 strm 内容(URL,原样保留,含签名参数),用 ffprobe 远程探测(`-show_format -show_streams`),提取内嵌标签(flac Vorbis / mp3 ID3 / m4a MP4,大小写不敏感、别名兼容)作为本地元数据;探测结果按 strm 文件修改时间缓存,重复扫描不重复探测。
2. **在线下载器 `IRemoteMetadataProvider<Audio, SongInfo>`(MusicBrainz)**:引擎在本地元数据之后调用——内嵌带可信 MBID 则按 ID 精确取回;无 MBID 或 MBID 与标题不符则按标题在 MusicBrainz 搜索(候选层做标题宽松匹配 + 艺术家宽松匹配);MusicBrainz 不可用/无结果时**不做在线补全**,保留内嵌字段(需自备 MB 连通,如镜像/代理)。
3. **合并(在线优先)**:内嵌标签可能由用户自填/有误,故**任何 MusicBrainz 在线命中**——MBID 精确、文本唯一高置信、模糊多候选的 best 候选——都用在线字段覆盖内嵌重叠字段;仅在线缺失的字段回填内嵌(MusicBrainz ID 除外,宁缺毋滥防脏 ID);在线无果 → 内嵌兜底。
4. **持久化/组织/封面**:引擎统一保存(含 `Album` 等受管字段的关联)、生成 MusicAlbum / MusicArtist 聚合;封面经 `GetImages` 由引擎下载并挂到条目。

## 触发方式(无需计划任务、无需 PostScan)

- **入库/刷新即处理**:插件注册的 `ILocalMetadataProvider` + `IRemoteMetadataProvider` 由 Emby 刷新引擎在**新 .strm 入库扫描、媒体库扫描、任何"刷新元数据"操作**时自动调用;本地探测 → 在线补全 → 引擎持久化与专辑归组,全程标准流程,无自写库、无双写循环。
- 刷新引擎自带 remote 结果缓存,重复刷新不会重复请求 MusicBrainz。
- 无任何定时/计划任务。

## 实测结果(Emby 4.9.5.0,MB-only 版)

- 21 条 strm(周杰伦《七里香》《叶惠美》):本地探测 21/21;MusicBrainz 在线命中并写回 **17~19/21 条**(其余在官方源间歇 503 时保留内嵌),失败=0;写回的 `MusicBrainzTrack/Album/Artist` 为**真实 MBID**(不再回填内嵌脏值 `f13d05fa…`/`ac2b0f62…`),艺术家 MBID `a223958d`(周杰倫)正确。
- 中文曲目命中正确:`我的地盤`、`七里香`、`將軍`、`擱淺` 等(标题保持内嵌繁体,字段来自 MusicBrainz)。
- **循环根治**:旧版 Provider+PostScan 双写不一致导致的循环已随重构移除;写回与组织由 Emby 引擎单点完成。
- 专辑组织:写入由引擎统一持久化后,Emby 原生生成/刷新 `MusicAlbum` / `MusicArtist`;专辑 MBID 由 Emby 内置 MusicBrainz 抓取器在刷新时写入可达专辑。

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
  Online/     MusicBrainzApi / OnlineResolver(MBID→文本编排,含限速与熔断)
  Providers/  MusicStrmLocalProvider(ILocalMetadataProvider,探测内嵌) / MusicStrmRemoteProvider(IRemoteMetadataProvider,MB 在线+封面)
tests/        xunit 单测(TagParser / ProbeResult / MergePolicy)
```
