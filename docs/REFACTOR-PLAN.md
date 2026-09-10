# MusicStrmExtract 重构方案

日期：2026-09-11

## 0. 目标与结论

本方案采用“原地渐进式重做”，不进行 greenfield 全量推倒重写。

原因：

- Emby Provider 生命周期、MusicBrainz 选版规则和旧库修复规则包含大量历史约束。
- 当前 129 个单元测试全部通过，可以作为行为基线。
- 主要问题是职责耦合和测试夹具不合理，不是代码无法继续维护。
- 分阶段替换可以保证每一步都能独立验证、独立回滚。

本方案的最终目标是：

1. `MusicStrmLocalProvider` 只做 Emby 适配。
2. 专辑定位、搜索策略、网络访问、缓存和 UI 修复各自有明确边界。
3. 核心选版规则可以脱离 HTTP、Emby 和文件系统单测。
4. 网络、缓存和 Provider 组装可以替换，而不影响选版规则。
5. 所有迁移阶段保持当前外部行为不变。

## 1. 当前工程快照

### 1.1 工程结构

- 主项目：`MusicStrmExtract/MusicStrmExtract.csproj`
- 目标框架：`.NET 8`
- 主要依赖：`MediaBrowser.Server.Core 4.9.1.90`
- 单元测试：`tests/MusicStrmExtract.Tests`
- 集成/E2E 测试：`tests/MusicStrmExtract.IntegrationTests`

当前基线：

- 主项目 Release 构建成功。
- 单元测试 129/129 通过。
- 主要业务代码约 2,000 行。
- Provider 编排、Plugin 启动和 UI 后台任务缺少直接单元测试。

### 1.2 两个外部入口

1. Emby 元数据读取入口
   - `MusicStrmLocalProvider.GetMetadata`
   - Emby 扫描或刷新 `Audio` 时调用。

2. 插件配置页入口
   - `Plugin` 注册 `MusicStrmPageController`。
   - `MusicStrmPageView.RunCommand`
   - “运行旧库修复”按钮调用 `StaleMusicAlbumRepairService`。

## 2. 当前端到端调用链

### 2.1 `.strm` 元数据定位链

```text
Emby
  -> MusicStrmLocalProvider.GetMetadata
     -> 检查 .strm 后缀
     -> StrmFileParser.GetFolderStructure
        -> 得到 albumFolder / artistFolder / albumDir / folderDisc
     -> StrmFileParser.ParseFileName
        -> 得到 fileDisc / trackNumber / isCommentary
     -> AlbumDirectoryScanner.Scan
        -> 扫描专辑目录和 Disc N 子目录
        -> 归并评论轨/正式轨轨号
        -> 生成 LocalDisc[]
     -> AlbumTrackMapLocator.GetOrSearchAsync
        -> TtlCache 查询
        -> per-key single-flight
        -> 为 miss 创建 MusicBrainzApi
        -> AlbumSearch.SearchForTrackMapAsync
           -> 清洗专辑名，解析本地年份
           -> 搜索 release 候选
           -> 搜索候选稳定排序
           -> 查询 top-1 release-group
           -> RG 内 release 分层评分
           -> 拉取 release 详情并校验本地碟轨布局
           -> 优先返回 exact
           -> top-1 RG 无 exact 时，回退检查其它搜索候选
           -> 构建 AlbumSearchResult
        -> 写入 TtlCache
     -> AudioTrackMetadataFactory.TryBuild
        -> 再次校验碟轨布局
        -> 定位单条 track
        -> 构建 Audio 和 ProviderIds
     -> 返回 MetadataResult<Audio>
```

关键点：

- Provider 不直接写 Emby 库。
- Provider 只返回 `MetadataResult<Audio>`。
- MusicAlbum、MusicArtist 和关联关系由 Emby 合并保存。
- `.strm` 路径之外直接返回空结果。

### 2.2 旧库修复链

```text
Emby 设置页按钮
  -> MusicStrmPageView.RunCommand
     -> 防重入检查
     -> 创建 CancellationTokenSource
     -> Task.Run
        -> StaleMusicAlbumRepairService.Run
           -> 防重入检查
           -> 检查媒体库扫描状态
           -> 快照 Audio 和 MusicAlbum
           -> 计算 staleAlbums
              -> 无 MusicBrainzAlbum
              -> 无文件路径
              -> 无 Audio.AlbumId 引用
           -> 删除 staleAlbums，循环中检查扫描和取消
           -> 计算需要刷新的 .strm
              -> 指向陈旧 MusicAlbum
              -> 或没有 AlbumId 但已有 MusicBrainzAlbum
           -> 按 FullRefresh 排队
        -> 同步更新 UI Label
```

## 3. 当前职责地图

| 区域 | 当前类型 | 当前职责 | 主要问题 |
|---|---|---|---|
| Plugin 入口 | `Plugin` | Emby 生命周期、配置、UI 注册、旧 XML 迁移 | 组合根不明确 |
| Provider | `MusicStrmLocalProvider` | Emby 适配、目录解析、缓存入口、错误边界、结果返回 | 自己创建 locator，静态缓存隐藏生命周期 |
| 本地扫描 | `AlbumDirectoryScanner` | 文件枚举、轨号归一化、本地碟构建 | 与特定目录规则耦合，但边界基本合理 |
| 定位缓存 | `AlbumTrackMapLocator` | 缓存、single-flight、API 创建、搜索调用、日志 | 同时承担缓存和用例编排 |
| 搜索编排 | `AlbumSearch` | 名称清洗、候选排序、RG 查询、回退、详情获取、结果构建 | 职责过多，最需要拆分 |
| RG 评分 | `ReleaseGroupScorer` | 国家推断、质量分、分层排序键 | 规则集中，但用长整数拼 rank，语义不够显式 |
| 布局校验 | `ReleaseLayoutMatcher` | 本地碟与 MB media 映射、exact 判定 | 边界合理，建议保留 |
| MusicBrainz | `MusicBrainzApi` | URL、限流、响应缓存、HTTP、JSON 解析 | 网络基础设施和类型化 API 混在一起 |
| 结果构建 | `AudioTrackMetadataFactory` | 找 track、构建 Audio、ProviderIds | 边界合理，建议保留 |
| UI 修复 | `StaleMusicAlbumRepairService` | 查询、计划、删除、刷新、进度、取消 | 纯策略和副作用执行未分离 |

## 4. 必须保持不变的行为契约

以下规则在结构重构阶段不能被“顺手优化”。

### 4.1 Emby 集成边界

- `MusicStrmLocalProvider` 继续实现 `ILocalMetadataProvider<Audio>`。
- 不注册 `IRemoteMetadataProvider` 或 `IRemoteImageProvider`。
- Provider 不调用 `UpdateToRepository` 或任何直接写库 API。
- 封面继续交给 Emby 内置 MusicBrainz 图像获取器。
- UI 页通过 `IHasUIPages` 显式实现，按钮命令一致。
- 普通音频文件不进入插件元数据链。

### 4.2 专辑定位缓存

- TTL 为 30 分钟。
- 最大容量为 500。
- 过期项按插入顺序惰性清理。
- 超过容量时只淘汰最旧条目。
- key 必须包含专辑、艺人、本地碟布局和 `MusicBrainzBaseUrl`。
- 同一 key 的并发请求保持 single-flight。

### 4.3 搜索候选排序

搜索候选当前稳定顺序：

1. 状态优先级
2. `primary-type == Album`
3. 完整日期优先
4. 日期较早优先
5. 偏好国家
6. MusicBrainz score 降序
7. 标题 stable sort
8. release id stable sort

状态分类和权重必须继续来自 `ReleaseStatusPolicy`。

### 4.4 Release-group 选版

RG 选版当前分层顺序：

1. 状态
2. 年份贴近
3. 国家偏好
4. 日期与质量分
5. release id 稳定排序

补充规则：

- 国家偏好只影响官方状态候选。
- 国家偏好不能把 Bootleg/Pseudo/Withdrawn 抬到官方版本前。
- `XW` 等非市场国家不参与国家偏好推断。
- 视频 media 不计入音频 exact 判定。
- exact 要求本地映射覆盖全部音频 media，并且逐碟轨数一致。
- 找到首个 exact 立即返回，不继续请求同档候选。
- 当前 RG 无 exact 时，继续回退搜索其它 RG 候选。
- 同一轮搜索内相同 release 只评估一次。

### 4.5 MusicBrainz 请求

- 所有请求继续经过 `GetJsonRootAsync` 等价入口。
- 限流器继续覆盖“间隔等待 + 完整 HTTP 请求”。
- 间隔保持 1100 ms。
- 限流器继续为进程级共享，不是 API 实例级。
- HTTP 请求失败不能写入成功缓存。
- 网络故障不能伪装成“确认未命中”并锁定 30 分钟。

### 4.6 评论轨和目录规则

- 支持同轨号评论轨。
- 支持评论轨在前、在后。
- 支持完整奇偶交错轨号映射。
- 部分交错无法确定时保留原轨号。
- 专辑文件夹名只剥离末尾发行年份。
- 同名自名专辑和纯年份标题不能被误清洗。

### 4.7 旧库修复

- 只删除无 MBID、无路径、未被 Audio 引用的 MusicAlbum。
- 删除和排队循环持续检查媒体库扫描状态。
- 取消请求必须能中止删除和刷新循环。
- 不在 Provider 链路自动触发旧库修复。

## 5. 当前主要结构问题

### 5.1 `AlbumSearch` 责任过多

`AlbumSearch` 当前同时处理：

- 搜索输入标准化
- 搜索候选排序
- release-group 查询
- release 详情查询
- exact/fallback 决策
- release 去重
- 最终结果构建

这导致新增一个选版规则时，必须同时理解网络调用顺序、回退路径和结果对象。

### 5.2 Provider 组装不可替换

`MusicStrmLocalProvider` 内部直接创建 `AlbumTrackMapLocator`。

结果：

- Provider 无法通过 fake locator 单测完整编排。
- 静态 `TtlCache` 隐藏了生命周期。
- 测试必须间接覆盖 Provider 的一部分行为。

### 5.3 缓存和用例编排混在一起

`AlbumTrackMapLocator` 同时管理：

- TTL cache
- per-key inflight
- API factory
- 搜索调用
- 日志

缓存策略和专辑搜索用例应拆开，至少通过接口隔离。

### 5.4 MusicBrainz 基础设施混入类型化 API

`MusicBrainzApi` 同时管理 URL、限流、HTTP、响应缓存和解析。

这使以下测试必须组合在一起：

- URL 是否正确
- 限流是否获取
- 响应是否缓存
- JSON 是否解析正确

阶段目标不是马上更换 HTTP 栈，而是先把边界明确，后续再决定响应缓存是否有界、是否加 TTL。

### 5.5 修复服务同时规划和执行

`StaleMusicAlbumRepairService` 的“删除哪些专辑、刷新哪些文件”是纯策略，但当前和 Emby 查询、删除、刷新、进度产生耦合。

应拆成：

- `RepairPlanBuilder`
- `RepairExecutor`
- `MusicStrmPageView` 只管 UI 状态

### 5.6 测试夹具偏向 JSON 字符串

`AlbumSearchSelectionTests` 通过大段 JSON 字符串构造 release 和 RG。

问题：

- 测试意图被 JSON 结构淹没。
- 新增一条选版用例需要同时维护三个字符串构造器。
- 规则测试和解析测试耦合。

重构后应增加领域对象 builder，把解析测试留在 parser 测试。

## 6. 目标架构

```text
Plugin
  -> PluginRuntime / composition root
     -> IAlbumResolutionService
     -> StaleMusicAlbumRepairService

MusicStrmLocalProvider
  -> IAlbumResolutionService
  -> AudioTrackMetadataFactory

AlbumResolutionService
  -> TtlCache
  -> SingleFlight
  -> IAlbumSearchEngine
  -> IMusicBrainzApi factory

AlbumSearchEngine
  -> SearchCandidateOrderingPolicy
  -> ReleaseGroupSelectionPolicy
  -> ReleaseCandidateEvaluator
  -> AlbumSearchResultFactory

MusicBrainzApi
  -> MusicBrainzRequestExecutor
  -> ReleaseJsonReader / ReleaseTracklistParser

StaleMusicAlbumRepairService
  -> RepairPlanBuilder
  -> RepairExecutor
```

### 6.1 目标职责

| 类型 | 只负责 |
|---|---|
| `PluginRuntime` | 创建进程级服务，明确缓存和 API factory 生命周期 |
| `MusicStrmLocalProvider` | Emby 输入/输出适配、异常转空结果 |
| `IAlbumResolutionService` | 给定相册请求，返回缓存或搜索结果 |
| `AlbumResolutionService` | cache、single-flight、调用 search |
| `IAlbumSearchEngine` | 专辑搜索用例入口 |
| `AlbumSearchEngine` | 编排搜索、RG、回退与结果选择 |
| `SearchCandidateOrderingPolicy` | 搜索候选 stable ordering |
| `ReleaseGroupSelectionPolicy` | 国家推断和 RG 内排序 |
| `ReleaseCandidateEvaluator` | 拉详情、布局匹配、去重 |
| `AlbumSearchResultFactory` | 构造不可变结果，保留组级 fallback |
| `IMusicBrainzApi` | 类型化 MusicBrainz 能力 |
| `MusicBrainzRequestExecutor` | URL 之外的 gate、transport、cache、错误 |
| `RepairPlanBuilder` | 根据快照生成纯删除/刷新计划 |
| `RepairExecutor` | 执行删除、排队、进度、取消、扫描检查 |

### 6.2 明确不引入的内容

- 不引入 MediatR。
- 不引入通用 Repository。
- 不建立大量无行为的空接口。
- 不改变 Emby 插件注册方式。
- 不增加远程图片 Provider。
- 不把缓存改成数据库或持久化方案。
- 不在结构迁移阶段同时改选版规则。

## 7. 分阶段重做计划

### 阶段 0：冻结行为

目标：

- 把当前行为写成可验证的契约。
- 不修改生产逻辑。

工作：

1. 记录当前测试基线：129/129。
2. 将本方案第 4 节作为迁移检查表。
3. 增加领域对象 test builder，减少 JSON 字符串。
4. 为 Provider 和 UI 编排补空壳测试。
5. 建立代表性选版矩阵：
   - 普通版 vs 豪华版
   - 多碟完整匹配
   - 只有主碟
   - 视频 bonus
   - 评论轨交错
   - 同名专辑和标题年份
   - 国家多数偏好
   - Bootleg/Pseudo 状态

验收：

- 旧测试数量不减少。
- 新增测试只描述现状，不修改现状。
- 记录每个测试锁定的规则。

### 阶段 1：Provider 组装和缓存边界

目标：

- Provider 不再自己创建定位服务。
- 缓存生命周期从静态字段移到明确组合根。

建议接口：

```csharp
internal sealed record AlbumResolutionRequest(
    string AlbumFolder,
    string? ArtistFolder,
    IReadOnlyList<LocalDisc> LocalDiscs,
    string? MusicBrainzBaseUrl);

internal interface IAlbumResolutionService
{
    Task<AlbumSearchResult> ResolveAsync(
        AlbumResolutionRequest request,
        CancellationToken ct);
}
```

改动：

1. `AlbumTrackMapLocator` 迁移为 `AlbumResolutionService`。
2. cache key 构建和请求去重隐藏在服务内部。
3. Provider 内部构造注入 `IAlbumResolutionService`。
4. Emby 兼容的公开构造函数保留，但委托给默认运行时。
5. `TtlCache` 由 composition root 创建。

必须保持：

- cache key 等价。
- TTL、容量和驱逐行为不变。
- single-flight 不变。
- 网络异常处理位置不变。

验收：

- 新增 Provider 假 locator 测试。
- 129 个旧测试继续通过。
- `MusicStrmLocalProvider` 不再直接引用 `MusicBrainzApi`。

### 阶段 2：拆 `AlbumSearch`

目标：

- `AlbumSearch` 只保留用例编排。
- 排序、匹配、结果构建分别可测。

改动顺序：

1. 提取 `SearchCandidateOrderingPolicy`。
2. 提取 `AlbumSearchResultFactory`。
3. 提取 `ReleaseCandidateEvaluator`。
4. 提取 `ReleaseGroupSelectionPolicy` 或保留现有 scorer 作为其实现。
5. `AlbumSearch` 保留现有公开入口，作为兼容 Facade。

建议内部结构：

```text
AlbumSearchEngine
  SearchAsync(request)
    -> search candidates
    -> order candidates
    -> evaluate top release-group
    -> if exact return
    -> evaluate ordered fallback
    -> build result
```

必须保持：

- 搜索候选 10 条上限。
- top-1 RG 优先。
- RG 无 exact 后继续检查其它搜索候选。
- exact 命中立即返回。
- 同一 release 只评估一次。
- fallback 使用首个可用候选。

验收：

- `AlbumSearchSelectionTests` 全部通过且断言不变。
- 新 policy 组件有独立测试。
- `AlbumSearch` 中不再同时出现具体排序算法和 HTTP 调用顺序。

### 阶段 3：拆 MusicBrainz 请求边界

目标：

- 类型化 API 与请求基础设施分离。

第一刀只做机械拆分：

1. URL builder 独立。
2. `GetJsonRootAsync` 迁入 request executor。
3. `MusicBrainzApi` 保留类型化端点。
4. 测试继续注入 `IHttpTransport` 和 `IRequestGate`。

第一刀不改：

- 1100 ms 间隔。
- 共享 gate。
- 响应缓存语义。
- 错误类型和状态码处理。

后续独立决策：

- 响应缓存是否加 TTL。
- 响应缓存是否需要容量上限。
- 是否增加有限重试。
- 是否统一记录请求耗时和状态。

验收：

- `MusicBrainzApiTests` 全部通过。
- 请求 URL、gate 获取次数和缓存命中次数与现状一致。
- 不在同一提交中调整缓存过期策略。

### 阶段 4：拆分旧库修复

目标：

- 纯计划与副作用执行分离。

建议模型：

```csharp
internal sealed record RepairPlan(
    IReadOnlyList<MusicAlbum> AlbumsToDelete,
    IReadOnlyList<Audio> StrmToRefresh);
```

改动：

1. `RepairPlanBuilder` 接收 Audio/MusicAlbum 快照，返回计划。
2. `RepairExecutor` 执行计划。
3. `StaleMusicAlbumRepairService` 只组合 builder 和 executor。
4. `MusicStrmPageView` 只管理命令、取消和 UI 状态。

必须保持：

- 删除判定不变。
- 刷新判定不变。
- 删除和排队循环内继续检查扫描状态。
- 进度顺序和最终结果不被旧进度覆盖。

验收：

- plan builder 可做纯单元测试。
- executor 可注入删除、排队、扫描状态。
- 现有 Repair tests 全部通过。

### 阶段 5：测试和工程配置收敛

目标：

- 让测试告警反映真实质量风险。

工作：

1. 测试项目单独配置分析器规则。
2. 保留生产代码的严格分析器设置。
3. 将 API integration tests 与 live Emby E2E tests 明确分开。
4. 未配置 Emby 环境时输出跳过，而不是静默通过。
5. 在 CI 中固定执行：
   - Release build
   - unit tests
   - 可选的 MusicBrainz integration tests
   - 可选的真实 Emby 刷新测试

验收：

- 生产构建无新增 warning。
- 测试 warning 数量显著下降。
- CI 能明确区分 passed、skipped 和 failed。

### 阶段 6：真实媒体验证

目标：

- 证明结构调整没有改变 Emby 落库结果。

单条验证：

1. 备份已安装 DLL。
2. 部署新构建 DLL。
3. 刷新一个 `.strm`。
4. 读取 API 字段：
   - `ProviderIds`
   - `AlbumId`
   - `AlbumArtist`
5. 检查日志中的 release MBID、recording MBID、碟号和轨号。
6. 确认 MusicAlbum 和 MusicArtist 链接正确。

全量验证：

1. 选择代表性 5 到 10 张专辑。
2. 对比刷新前后的 release MBID、recording MBID 和 AlbumId。
3. 覆盖普通版、豪华版、多碟、评论轨、同名专辑。
4. 验证旧库修复按钮仍只删除符合条件的专辑。

回滚：

- 每个阶段使用独立 commit。
- 出现选版差异时只回滚该阶段。
- 不改数据库结构，因此不需要数据迁移。

## 8. 测试策略

### 8.1 单元测试分层

| 层级 | 内容 |
|---|---|
| 纯解析 | 文件路径、track number、album name、JSON parser |
| 纯策略 | 搜索排序、RG 评分、国家推断、布局 exact |
| 用例编排 | AlbumSearch、Provider、RepairPlanBuilder |
| 缓存与并发 | TtlCache、single-flight、cancellation |
| 基础设施 | URL、gate、transport、response cache |

### 8.2 必须新增的测试

- `MusicStrmLocalProvider` 非 `.strm` 返回空结果。
- Provider 使用假 resolver 成功构建 Audio。
- Provider 遇到 MB 网络错误不缓存结果。
- Provider 遇到取消时向上传播取消。
- `IAlbumResolutionService` 的 cache key 兼容测试。
- Repair plan builder 的删除和刷新决策。
- Repair executor 的扫描中断、取消和进度顺序。
- 真实 Emby 刷新后校验 ProviderIds 和 AlbumId。

### 8.3 测试夹具方向

新增：

- `ReleaseSummaryBuilder`
- `ReleaseMediaBuilder`
- `AlbumSearchRequestBuilder`
- `RepairSnapshotBuilder`

原则：

- 选版测试使用领域对象，不手工拼接 JSON。
- JSON 形态只在 parser tests 和 API tests 中出现。
- 测试名称保留业务行为，不命名 private implementation。

## 9. 提交拆分建议

建议 PR 顺序：

1. `test: add provider and selection characterization coverage`
2. `refactor: introduce album resolution service`
3. `refactor: extract search candidate ordering`
4. `refactor: extract release evaluation and result factory`
5. `refactor: extract musicbrainz request executor`
6. `refactor: split repair planning from execution`
7. `chore: scope analyzers for test project`

每个 PR 只允许一个主要结构变化。

## 10. 验收标准

每个重构 PR 都必须满足：

- `dotnet test tests\MusicStrmExtract.Tests\MusicStrmExtract.Tests.csproj -c Release --no-restore --nologo` 通过。
- 主项目 Release build 成功。
- 没改变 Provider 只读返回语义。
- 没改变缓存 key、TTL、容量和驱逐策略。
- 没改变 MusicBrainz 限流语义。
- 没改变选版分层和 exact 提前返回。
- 没改变旧库修复删除条件。
- 真实 Emby 验证差异有明确原因，否则回滚。

## 11. 暂不处理的优化

以下内容不混入结构重构：

- 更换 MusicBrainz 客户端或第三方库。
- 增加新的元数据来源。
- 改变 release-group 选版规则。
- 增加数据库迁移。
- 引入持久化缓存。
- 增加并发度或调整 1100 ms 限流。
- 调整插件版本号。
- 修改用户配置格式。

它们应在结构重构完成、行为验证稳定后作为独立需求评估。

## 12. 执行状态（2026-09-11）

本方案的主要结构任务已执行完成：

- Provider 已通过 `IAlbumResolutionService` 与缓存/定位实现解耦。
- `AlbumSearch` 已拆出搜索候选排序、release 候选评估和结果构建。
- MusicBrainz URL 构造与请求执行已从类型化 API 中拆出。
- 旧库修复已拆成 `RepairPlanBuilder`、`RepairExecutor` 和 `RepairJobRunner`。
- Provider、搜索策略、请求执行、修复计划和后台任务已补直接单元测试。
- 测试工程已缩小分析器噪声，生产项目保持 0 warning。
- 集成测试在缺少环境时明确跳过；Emby 和 MusicBrainz live tests 改为显式启用。

验证结果：

- 主项目 Release Rebuild：0 warning，0 error。
- 单元测试：146/146 通过。
- 默认集成测试：1 passed，12 skipped。
- 本机 Emby E2E：8/8 通过。
- MusicBrainz 镜像集成测试：5/5 通过。
- 官方 MusicBrainz 在验证时返回 HTTP 503，使用已配置镜像完成复验。
- 新构建 DLL 已临时部署并刷新单个 `.strm`：
  - release MBID、recording MBID、release-group MBID 和 AlbumId 刷新前后一致。
  - Emby 日志确认 `RefreshItem Complete`。
  - 验证后已恢复原安装 DLL。
