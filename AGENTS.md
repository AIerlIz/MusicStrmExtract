# Agent Instructions

## Proxy
代理、Emby 地址与凭据只保存在本机未跟踪的 `AGENTS.local.md`（已加入 `.gitignore`）。
本文件不填写实际值，也不要把 `AGENTS.local.md` 的内容复制进本仓库。

## Emby Server (端到端测试)
需要外网或 Emby 端到端验证时，先读取本机 `AGENTS.local.md` 中的对应值。

## 开发与维护注意事项

### 本地 Provider 不再直写库
- `MusicStrmLocalProvider` 已移除 `SyncRepositoryItem`，定位成功后只返回 `MetadataResult<Audio>`，由 Emby 合并保存。不要再加回 Provider 内 `UpdateToRepository` 直写。
- Emby 4.9.5 实测：`Audio.Album` 是 `AlbumItem?.Name` 的计算字段，字符串不会单独落库；专辑关系以 `AlbumId -> MusicAlbum` 保存。Provider 返回带 `Album` + `AlbumArtists` + `MusicBrainzAlbum` 的 Audio 后，`SqliteItemRepository.SaveAlbumIfNeeded` 会自动创建/复用虚拟 `MusicAlbum` 并链接 `MusicAlbumArtist`。
- 若看到 `Album`/`AlbumArtists` 为空而 MBID 已有，通常不是 Provider 问题，而是条目尚未真正走一遍完整元数据刷新（库扫描只在文件变化时重跑 Provider；旧库需要触发一次刷新/扫描，或重新添加条目）。
- 修改返回字段或落库相关代码时，用可回滚方式部署 DLL，刷新单个 `.strm`，读 `programdata/data/library.db`（或 API `Fields=AlbumArtist,ProviderIds`）确认 `AlbumId` 与 MusicAlbum 实体出现后再扩到全量。

### Cover Art 镜像地址
- `CoverArtBaseUrl` 同时影响两处：本地 Provider 选版时的 CAA 封面数查询，以及 RemoteProvider 返回给 Emby 的最终封面下载 URL。
- 配置镜像时地址应以 `/` 或 `/release` 结尾；留空则使用官方 `https://coverartarchive.org/release/`。
- 后续新增封面相关代码时不要写死官方地址，应继续通过 `CoverArtClient.BuildFrontImageUrl` 生成。

### Remote Provider 只服务 .strm
- `MusicStrmRemoteProvider` 只负责 `.strm` 封面：普通音频自带标签，不交给本插件做在线补全。
- 修改 `GetMetadata`/`GetImages` 时保留 `.strm` 路径过滤，参考 `IsStrmPath`。
- `OnlineResolver`/`MergePolicy` 已随普通音频在线补全链路一起移除，不要按 recording/text search 重新引入。

### 专辑定位缓存
- `MusicStrmLocalProvider.AlbumCache` 使用通用 `TtlCache`：TTL 30 分钟、容量 500。
- 过期项按插入序惰性清理，超容量只淘汰最旧条目；不要改成每次全表遍历或整体清空。
- 缓存 key 包含 `MusicBrainzBaseUrl`/`CoverArtBaseUrl`，切换镜像后不会命中旧结果。
- 修改缓存语义时同步维护 `TtlCacheTests`。

### 配置页运行按钮
- `Plugin` 仍继承 `BasePluginSimpleUI<PluginConfiguration>` 负责 JSON 配置存储，并在类声明中显式列出 `IHasUIPages`，通过公开的 `UIPageControllers` 属性重实现接口，覆盖基类的 Simple UI 页面；只写属性而不重新声明接口不会生效。
- 配置页按钮命令由 `MusicStrmPageView.RunCommand` 转发到 `StaleMusicAlbumRepairService`；不要在 Provider 链路里直接调用修复逻辑。
- 修复只删除无 `MusicBrainzAlbum`、无文件路径、且未被任何 Audio `AlbumId` 引用的 `MusicAlbum`，随后排队刷新相关 `.strm`；不要改成无确认地批量删库。
- 页面新增按钮时，确认 `ButtonItem.CommandId` 与 `MusicStrmPageView.RunCommand` 的分支一致。

### RG 选版与请求收敛
- `AlbumSearch.SearchForTrackMapAsync` 会先检查 top-1 候选所在的 release-group；若当前 RG 没有轨数完全一致的 exact 命中，会继续检查搜索候选里其它 RG 的精确命中。
- 找到首个 exact 后，只继续拉取真正同档的候选用于 CAA 决胜，避免逐个请求整个 release-group 的完整 tracklist。
- 同分且双方 release 都缺年份/日期时仍属同档，应继续收集并交给 CAA 决胜。
- 国家偏好只作用于最高基础分档，不能把低状态版本的分数抬到官方版本之上。
- 搜索候选状态排序与 RG 评分统一在 `ReleaseStatusPolicy.SearchPriority` / `ScoreWeight` 维护，不要另写一套字符串分类；修改优先级时同步 `ReleaseStatusPolicyTests`、`AlbumSearchSelectionTests` 和 `ReleaseGroupScorerTests`。
- 修改这些排序、提前返回或断点逻辑时，同步维护 `AlbumSearchSelectionTests` 和 `ReleaseGroupScorerTests`。

### MusicBrainz 限流
- `MusicBrainzApi` 的静态信号量覆盖“间隔等待 + 完整 HTTP 请求”，避免上一请求未结束时并发打向 MusicBrainz。
- 请求间隔常量为 `MinimumRequestIntervalMs`（1100ms）；不要改回“只锁等待间隔、释放后再发请求”的实现。
- 涉及新增 MusicBrainz 请求入口时统一走 `GetJsonRootAsync`，不要绕过缓存与限流直接发 `HttpClient`。

### 发版与验证
- 项目版本号需要手动同步：发新 tag 前更新 `MusicStrmExtract.csproj` 的 `Version`、`AssemblyVersion`、`FileVersion`。当前已同步为 `1.7.0.0`。
- 常规验证命令：`dotnet test tests\MusicStrmExtract.Tests\MusicStrmExtract.Tests.csproj -c Release --no-restore --nologo`。
- 涉及封面、直写或刷新流程的改动，发布前建议连接 Emby Server 跑一次媒体库刷新。
