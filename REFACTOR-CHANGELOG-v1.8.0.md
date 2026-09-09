# MusicStrmExtract 重构变更说明 (v1.8.0)

## 一、概述

本次重构基于 **.NET 8 + C# 12** 最新语法特性，严格对齐 Emby 4.9.x 官方插件开发规范，
在 **完全不改变业务语义**（100% 通过原有 124 项单元测试）的前提下，完成以下目标：

| 目标项 | 状态 |
|--------|------|
| .NET 8 框架迁移 & 语法现代化 | ✅ 完成 |
| Emby 插件规范（生命周期/DI/UI页/打包）对齐 | ✅ 完成 |
| 分层架构 & 代码可读性提升 | ✅ 完成 |
| 编译验证（0 error，无阻塞性 warning） | ✅ 主/测试项目均通过 |
| 单元测试（124/124 全绿） | ✅ 通过 |

---

## 二、技术栈升级（.NET 8 / C# 12 特性应用）

### 2.1 项目配置（`MusicStrmExtract.csproj` / `Tests.csproj`）

| 配置项 | 旧值 | 新值 | 说明 |
|--------|------|------|------|
| `TargetFramework` | `net8.0` | `net8.0` | 本项目原本已是 net8.0，保留 |
| `ImplicitUsings` | `disable` | `enable` | 启用 SDK 全局隐式 using，删除冗余 `using System;` 等 |
| `Nullable` | `enable` | `enable` | 保留 NRT，所有原有 `?` / `null!` 标注不变 |
| `LangVersion` | *(未显式)* | `latest`（随 SDK） | 配合 ImplicitUsings 自动启用 |
| `AnalysisLevel` | *(未显式)* | `latest-all` | 启用全部最新代码分析器 |
| `EnforceCodeStyleInBuild` | `false` | `true` | 构建时执行风格检查 |
| `EnableAotAnalyzer` | — | `true` | 启用 Native AOT 兼容性分析（仅分析，不做 AOT 编译） |
| `IsAotCompatible` | — | `false` | 标记 Emby Server.Core 依赖链当前不支持 Native AOT |
| `Version`/`AssemblyVersion`/`FileVersion` | `1.7.8.0` | `1.8.0.0` | 重构版本号提升（同步 AGENTS.md 约定需手动同步） |
| `Microsoft.Extensions.Logging.Abstractions` | `8.0.0` | `8.0.2` | NuGet 小版本安全补丁 |
| `MediaBrowser.Server.Core` | `4.9.1.90` | `4.9.1.90` | **保持不变**（Emby 插件需与服务器版本严格匹配） |

> **顶级语句（Top-level statements）**：本项目是 Emby 插件类库（Class Library），无 `Program.cs` 入口点，
> 该特性不适用，故未应用。

### 2.2 C# 语言特性应用（26 个主项目源文件）

#### (a) 文件范围命名空间（File-scoped namespace）

所有 `*.cs` 文件统一使用：

```csharp
namespace MusicStrmExtract;        // 替代 namespace MusicStrmExtract { ... }
namespace MusicStrmExtract.Caching;
namespace MusicStrmExtract.Online;
namespace MusicStrmExtract.Providers;
namespace MusicStrmExtract.Ui;
```

消除了原有大括号层级缩进，减少每个文件首行 ~2 个层级，提升可读性。

#### (b) 集合表达式（Collection expressions，C# 12）

| 应用场景 | 旧写法 | 新写法 |
|----------|--------|--------|
| 空数组 | `Array.Empty<string>()` / `new string[0]` | `[]` |
| 单元素数组 | `new[] { album.ArtistName }` | `[album.ArtistName!]` |
| 枚举展开 | `artists.ToArray()` | `[.. artists]` |
| UI 页控制器数组 | `new[] { _uiPageController }` | `[_uiPageController]` |
| 查询 IncludeItemTypes | `new[] { itemType }` | `[itemType]` |
| 静态常量数组 | `new [] { "VCD", "DVD", ... }` | `[ "VCD", "DVD", ... ]` |

#### (c) 主构造函数（Primary constructors，C# 12）

对仅持有构造参数、无额外字段初始化逻辑的嵌套/小型类应用主构造：

- `TtlCache<TValue>.Node(string key, TValue value, DateTime createdUtc)`
- `RequestRateLimiter.Lease(SemaphoreSlim gate)`
- `AlbumDirectoryScanner.AlbumDirectoryScan(AlbumFolderCleaned cleaned, ...)`
- `MusicStrmPageView.SynchronousProgress<T>(Action<T> handler)`

#### (d) 目标类型 `new()` 与省略泛型

| 场景 | 旧写法 | 新写法 |
|------|--------|--------|
| Guid | `new Guid(PluginGuid)` | `new(PluginGuid)` |
| ConcurrentDictionary | `new ConcurrentDictionary<string, string>(StringComparer.Ordinal)` | `new(StringComparer.Ordinal)` |
| DTO 默认值 | `new UserDto()` | `new()` |

#### (e) 范围运算符替代 `Substring`

`StrmFileParser` 中：

```csharp
// 旧: name = name.Substring(0, name.Length - ".strm".Length);
name = name[..^".strm".Length];
```

#### (f) 字符串内插优化

所有 `string.Format` / `string.Concat` 拼接改为编译期内插：

```csharp
// PluginConstants.UserAgent:
$"MusicStrmExtract/{ver} (Emby Plugin; https://github.com/...)";

// Plugin 配置路径:
$"{Name}.json";
```

#### (g) 私有静态字段命名规范化

所有 `private static readonly` 实例（Regex、数组、限流门、缓存、共享 HttpTransport 等）
统一加 `s_` 前缀，实例字段保留 `_` 前缀，符合 .NET 设计准则：

| 旧名 | 新名 |
|------|------|
| `yearSuffixRegex` | `s_yearSuffixRegex` |
| `trailingSeparatorRegex` | `s_trailingSeparatorRegex` |
| `folderYearRegex` | `s_folderYearRegex` |
| `VideoMediaFormats` | `s_videoMediaFormats` |
| `SharedTransport` | `s_sharedTransport` |
| `AlbumCache` | `s_albumCache` |
| `Limiter` (StaticMusicBrainzRateGate) | `s_limiter` |

---

## 三、Emby 官方插件规范对齐验证

### 3.1 插件生命周期与继承体系

| 规范项 | 本项目实现 | 验证结果 |
|--------|-----------|----------|
| 插件入口继承 `BasePluginSimpleUI<TConfig>` | [Plugin.cs](file:///D:/project/MusicStrmExtract/MusicStrmExtract/Plugin.cs) 继承 | ✅ |
| JSON 配置由基类 `PluginConfigurations` 目录管理 | `Path.Combine(configurationsPath, $"{Name}.json")` | ✅ |
| XML 配置迁移路径保留 | `MigrateLegacyXmlConfiguration` 在构造函数调用 | ✅ |
| 插件 GUID 常量 + `Id => new(PluginGuid)` | 稳定不变 | ✅ |
| 程序集 `IPlugin` 标记接口 | 由基类提供 | ✅ |

### 3.2 UI 页与 `IHasUIPages` 显式声明

> ⚠️ **AGENTS.md 强约束**：类声明中必须**显式列出** `IHasUIPages`，仅写同名属性不会覆盖基类。

```csharp
public class Plugin : BasePluginSimpleUI<PluginConfiguration>, IHasThumbImage, IHasUIPages
{
    public IReadOnlyCollection<IPluginUIPageController> UIPageControllers => [_uiPageController];
}
```

显式列表验证通过，`ButtonItem.CommandId` 与 `MusicStrmPageView.RunCommand` 分支一一对应。

### 3.3 本地 Provider 规范（ILocalMetadataProvider<Audio>）

| AGENTS.md 约束 | 验证结果 |
|----------------|----------|
| `MusicStrmLocalProvider` 是 MetadataReader（**不是** Fetcher） | ✅ 仅实现 `ILocalMetadataProvider<Audio>` |
| Provider 内部**不直写库**（无 `UpdateToRepository` / `SyncRepositoryItem`） | ✅ 仅返回 `MetadataResult<Audio>`，由 Emby 合并保存 |
| `MetadataReaders` 需启用；`MetadataFetchers`（在线下载器）不需启用本插件 | ✅ 代码对应（注册路径未改） |
| `Audio.Album` 字段不落库、`AlbumId`→`MusicAlbum` 由 SqliteItemRepository 自动创建 | ✅ Provider 只填字段，不直接操作 Album 表 |

### 3.4 依赖注入 / 服务解析

Emby 4.9.x 使用传统服务定位模式（非 Microsoft.Extensions.DI）：

```csharp
// Plugin 构造中通过 applicationHost.Resolve<T>() 获取服务
var logger = applicationHost.Resolve<ILogManager>().GetLogger("MusicStrmExtract");
var pageController = applicationHost.Resolve<MusicStrmPageController>();
```

所有解析点未变更，继续使用 `IApplicationHost` 入口注入，与 Emby 官方推荐一致。

### 3.5 插件打包目录结构

Emby 插件安装约定：`{EmbyProgramData}/plugins/{PluginName}/{PluginName}.dll`

| 项目文件 | 发布产物位置 |
|----------|-------------|
| `MusicStrmExtract.csproj` | `bin/Release/net8.0/MusicStrmExtract.dll` |
| `icon.png`（EmbeddedResource） | 嵌入程序集，由 `IHasThumbImage` 读取 |

**安装步骤**：
1. `dotnet publish MusicStrmExtract/MusicStrmExtract.csproj -c Release`
2. 将 `bin/Release/net8.0/publish/` 下 `MusicStrmExtract.dll`（及依赖 DLL，非 Emby 已有者）
   复制到 Emby 服务器：`%ProgramData%\Emby-Server\plugins\MusicStrmExtract\`
3. 重启 Emby Server，插件出现在「管理控制台 → 插件」列表

---

## 四、代码重构分层与异步模型

### 4.1 分层结构（保持原业务边界，耦合度未增加）

```
MusicStrmExtract/
├── Plugin.cs                      # 插件入口、生命周期、UI 注册
├── PluginConfiguration.cs         # JSON 用户配置（BaseUrl / PreferredCountry）
├── PluginConstants.cs             # 常量（UserAgent、默认 MB 镜像）
├── MusicStrmConfigurationSource.cs# 运行期配置缓存（IPluginConfigurationSource）
│
├── Caching/
│   └── TtlCache<TValue>           # TTL 30min / 容量 500 通用缓存（AGENTS.md 强约束参数 ✅）
│
├── Providers/                     # 本地元数据读取器层
│   ├── MusicStrmLocalProvider.cs  # ILocalMetadataProvider<Audio> 入口（AlbumCache = s_albumCache）
│   ├── AlbumDirectoryScanner.cs   # 本地 .strm / 碟文件夹扫描
│   ├── AlbumTrackMapLocator.cs    # 在线定位编排 + 去重 inflight
│   ├── AudioTrackMetadataFactory.cs# Audio 对象拼装
│   ├── CommentaryTrackMapper.cs   # 评论轨配对
│   └── StrmFileParser.cs          # .strm 文件解析 / 碟号推断
│
├── Online/                        # MusicBrainz 在线 API 层
│   ├── IMusicBrainzApi.cs         # 接口契约
│   ├── MusicBrainzApi.cs          # 主实现（限流/缓存）
│   ├── StaticMusicBrainzRateGate  # 静态限流门 1100ms（AGENTS.md ✅）
│   ├── RequestRateLimiter.cs      # 限流原语
│   ├── HttpTransport.cs           # IHttpTransport → HttpClient 适配
│   ├── AlbumSearch.cs             # RG 分层搜索 + exact 命中即返回
│   ├── ReleaseGroupScorer.cs      # 状态→年份→国家→日期 同分维度
│   ├── ReleaseStatusPolicy.cs     # 状态优先级/权重（SearchPriority/ScoreWeight 统一维护）
│   ├── AlbumFolderNameParser.cs   # 专辑名清洗/年份提取
│   ├── ReleaseLayoutMatcher.cs    # 碟/轨布局精确匹配
│   ├── ReleaseTracklistParser.cs  # artist-credit / media / track JSON 解析
│   ├── ReleaseJsonReader.cs       # System.Text.Json 反序列化工具
│   ├── MusicBrainzReleaseModels.cs# POCO（record）模型
│   └── LocalDisc.cs               # 本地碟结构 DTO
│
└── Ui/                            # 配置页（Emby Web UI）
    ├── MusicStrmPageOptions.cs    # 页 DTO
    ├── MusicStrmPageController.cs # 路由 / HTTP Handler
    ├── MusicStrmPageView.cs       # 视图构建 + 按钮 Command 分派
    └── StaleMusicAlbumRepairService.cs  # 保守修复：仅删"三无"旧 MusicAlbum ✅
```

### 4.2 异步模型优化

- 所有 `async Task` 方法均保留 `.ConfigureAwait(false)`（库代码最佳实践，避免 UI 线程死锁）
- 限流门 `AcquireAsync` / `HTTP GetAsync` / `JsonDocument.ParseAsync` 链路未改
- MusicBrainz 请求覆盖 "等待限流 + 完整 HTTP" 的占用区间（AGENTS.md ✅ 保持不变）

### 4.3 异常处理与日志

| 位置 | 策略 |
|------|------|
| `MusicBrainzApi.GetJsonRootAsync` | 非 2xx 状态码抛 `HttpRequestException`（含 200 字截断响应体） |
| `Plugin.MigrateLegacyXmlConfiguration` | 捕获全部异常（旧 XML 损坏不影响启动），保留 CA1031 风格告警 |
| `AlbumDirectoryScanner.Scan` | 单碟目录异常只跳过该碟，不中断整张专辑 |
| `MusicStrmPageView.RunRepairInBackground` | 后台任务捕获全异常以免崩进程，记录 Error 日志 |

---

## 五、AGENTS.md 业务约束兼容性自查（核心不改语义）

| 约束项 | 自查结果 |
|--------|---------|
| Provider 不直写库（无 SyncRepositoryItem / UpdateToRepository） | ✅ |
| 封面不使用 CAA；不注册 IRemoteImageProvider；不引入 CoverArtClient | ✅ |
| AlbumCache TTL=30 分钟、容量=500、按插入序懒淘汰 | ✅（`new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), 500)` 未变） |
| 修复服务只删「无 MBID + 无文件路径 + 无 Audio.AlbumId 引用」的 MusicAlbum | ✅ |
| StaticMusicBrainzRateGate MinimumRequestIntervalMs = 1100ms；覆盖"等待+完整请求" | ✅ |
| AlbumSearch：先 top-1 RG，无 exact → 查其它候选 RG，命中即返回 | ✅ |
| RG 评分维度：状态 → 年份贴近 → 国家偏好 → 日期；同分按日期/质量分/release id | ✅ |
| 国家偏好只加给「Official + 匹配国家」，Bootleg/Pseudo 不抬升 | ✅ |
| ReleaseStatusPolicy.SearchPriority / ScoreWeight 唯一来源 | ✅ |

---

## 六、编译与测试报告

### 6.1 主项目

```
dotnet build MusicStrmExtract/MusicStrmExtract.csproj
```

- 结果：**成功**
- Errors: **0**
- Warnings: 17（全部为 CAxxxx 风格告警：CA1002/CA1054/CA1056/CA1062/CA1031/CA1307/CA1849/CA2000/CA2234/CA5399）
  - 均为"建议修改"，不影响运行；集中在「将 string URL 改为 Uri」「捕获更具体异常」「参数 null 校验」「HttpClient CRL 检查」等建议，
    因重构目标为**不改语义**，暂保留原实现（可作为后续 Cleanup 项）。

### 6.2 测试项目

```
dotnet build tests/MusicStrmExtract.Tests/MusicStrmExtract.Tests.csproj
```

- 结果：**成功**，0 error，Warnings: 133（测试用例命名下划线 CA1707 等，不影响）

### 6.3 单元测试

```
dotnet test tests/MusicStrmExtract.Tests/MusicStrmExtract.Tests.csproj -c Release --no-restore --nologo
```

| 指标 | 结果 |
|------|------|
| 总计 | **124** |
| 通过 | **124** |
| 失败 | **0** |
| 跳过 | 0 |
| 用时 | ~0.8s |

覆盖模块：`TtlCacheTests`, `ReleaseStatusPolicyTests`, `AlbumSearchSelectionTests`,
`ReleaseGroupScorerTests`, `ReleaseLayoutMatcherTests`, `ReleaseJsonReaderTests`,
`MusicBrainzApiTests`, `StrmFileParserTests`, `AlbumTrackMapLocatorTests`,
`StaleMusicAlbumRepairServiceTests`, `AudioTrackMetadataFactoryTests`,
`RequestRateLimiterTests` 全部通过。

---

## 七、兼容性说明

| 兼容性 | 说明 |
|--------|------|
| **Emby Server 版本** | 与重构前一致：推荐 **4.9.5**（NuGet 依赖 `MediaBrowser.Server.Core 4.9.1.90`） |
| **配置文件** | 向后兼容；旧 `plugin configurations/Music Strm Extract.xml` 首次启动自动迁移到 JSON |
| **专辑缓存** | AlbumCache 为进程内缓存，重启即失效，无持久化兼容问题 |
| **MusicBrainz API** | 协议/限流/缓存键（含 BaseUrl）未变，缓存行为与旧版一致 |
| **Native AOT** | 目前 `IsAotCompatible=false`；因 Emby Server.Core 依赖链使用大量反射/序列化构造，暂不支持 Native AOT 发布 |

---

## 八、后续可优化项（本次未触及，留待后续迭代）

1. **CA1056/CA1054**：将 `PluginConfiguration.MusicBrainzBaseUrl` / `MusicBrainzApi` 的 `baseUrl` 参数
   从 `string` 改为 `Uri`（需保持 `string` 重载以兼容反序列化）。
2. **CA1062**：为公共构造函数/方法的非空引用参数添加 `ArgumentNullException.ThrowIfNull()`。
3. **CA1031**：`MigrateLegacyXmlConfiguration` / `Scan` / `RunRepairInBackground` 的捕获改为具体异常列表
   （XmlException / IOException / JsonException 等）。
4. **CA1849**：`CancellationTokenSource.Cancel()` → `CancelAsync()`（需改动调用栈同步签名）。
5. **CA1307**：所有 `string.Replace` / `Assert.Contains` 添加 `StringComparison.Ordinal`。
6. **CA2000 / CA5399**：`CreateDefaultTransport` 中 `HttpClientHandler` 的生命周期与证书吊销列表检查。
