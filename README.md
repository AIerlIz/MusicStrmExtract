# Music Strm Extract

Music Strm Extract 是一个面向 Emby 的插件，用来给音乐库中的 `.strm` 文件补全音乐元数据。

`.strm` 文件本身只是一个文本链接，Emby 不会像普通音频文件那样探测它，所以默认只能看到一个文件名，无法生成歌手、专辑、曲目信息和 MusicBrainz ID。这个插件根据你的音乐库目录结构和文件名轨号，从 MusicBrainz 找到对应专辑和歌曲，并交给 Emby 保存、展示和归组。

## 功能

- 识别 `歌手 / 专辑 / 序号 - 标题.strm` 这类常规结构，自动锁定 MusicBrainz 专辑。
- 按轨号从 MusicBrainz 官方 tracklist 取回每首歌的标题、歌手、专辑、年份和 MBID。
- 不读取 `.strm` 文件里的 URL，不依赖 ffprobe 或远程媒体可播放。
- 扫描或刷新元数据时自动工作，不需要手动运行任务。
- 同专辑结果缓存 30 分钟，避免重复刷新时反复请求 MusicBrainz。
- 封面交给 Emby 从 Cover Art Archive 下载，不要求你手写 `cover.jpg`。

## 推荐目录结构

插件会按下面这样的两层结构工作，请把 `.strm` 直接放在专辑文件夹里：

```text
音乐库/
  周杰伦/
    叶惠美 (2003)/
      01 - 以父之名.flac.strm
      02 - 懦夫.flac.strm
      ...
    七里香 (2004)/
      01 - 我的地盤.flac.strm
      02 - 七里香.flac.strm
      ...
```

要点：

- 第一层是歌手文件夹，第二层是专辑文件夹，`.strm` 直接放在专辑文件夹里。
- 文件名前面是轨号，例如 `01 - `。轨号不要求和 Emby 内部曲目序号一致，但应当和 MusicBrainz 专辑的 track number 一致。
- 一张专辑内的轨号建议连续且不重复。
- 文件名标题部分不会被插件用来匹配，可以简写或省略；真正用于锁定专辑的是“歌手文件夹 + 专辑文件夹名”。
- 专辑文件夹名带年份也可以，例如 `叶惠美 (2003)`、`七里香-2004`，插件会自动去掉年份后缀。

如果 `.strm` 不在这种结构中，或者缺少轨号，目录主路径无法定位时，插件不会去读取 `.strm` 中的 URL 或探测远程媒体。

## 安装

1. 将构建产物 `MusicStrmExtract.dll` 复制到 Emby 的插件目录。

   Windows 示例：

   ```text
   C:\Users\<用户名>\AppData\Roaming\Emby-Server\programdata\plugins\
   ```

   Docker/Linux 安装请放到 Emby 对应的 `plugins` 目录。

2. 重启 Emby Server。
3. 在 Emby 插件列表中确认出现 “Music Strm Extract”。

插件没有外部 DLL 依赖，也不需要安装 ffprobe，只复制主 DLL 即可。

## 使用方式

插件会在以下时机自动处理：

- 新 `.strm` 文件入库扫描；
- 媒体库扫描；
- 手动对音频条目执行“刷新元数据”。

不需要创建计划任务，也不需要手动运行任何后台任务。

如果你要验证效果，最简单的方式是：

1. 将音乐库保持为推荐目录结构。
2. 在 Emby 中执行一次媒体库扫描或刷新音频元数据。
3. 打开 Audio 或 MusicAlbum，检查标题、歌手、专辑、年份、封面和 MusicBrainz 信息是否已经出现。

如果刷新前 Emby 里已经存在同名 MusicAlbum，旧的专辑归属关系可能不会自动重建。此时可以在 Emby 中删除对应 MusicAlbum 实体，再重新扫描媒体库。

## 配置

### Emby 设置页

1. 打开 Emby 管理后台，进入“服务器 → 插件”。
2. 在 “Music Strm Extract” 上点击“设置”。
3. 按需修改配置并保存，保存后无需重启 Emby。

设置页会自动根据以下配置项生成表单：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `MusicBrainzBaseUrl` | 空 | MusicBrainz 服务地址。留空使用官方 `https://musicbrainz.org`；网络不稳定时建议填写镜像，例如 `https://musicbrainz.emby.tv` |

### 配置文件位置

设置页保存的内容位于 Emby 的插件配置目录：

```text
<Emby 数据目录>/plugins/configurations/Music Strm Extract.json
```

如果你从旧版本升级，插件会在首次启动时自动读取旧的 `MusicStrmExtract.xml` 并迁移到上面的 JSON 文件，旧 XML 会保留在原处作为备份。

## 注意事项

- 插件只使用目录结构和文件名轨号取数，不读取 `.strm` 文件里的 URL，也不要求链接可访问。
- 插件只使用 MusicBrainz 作为在线数据源。MusicBrainz 不可达时不会从其它来源补全。
- 官方 MusicBrainz 在部分地区可能不稳定，建议配置稳定的镜像或代理。
- 目录主路径目前主要面向单碟专辑。多碟、分碟目录或同一专辑文件夹内轨号不唯一时，目录映射可能无法命中，请按单碟目录整理。
- 封面来自 Cover Art Archive，需要 MusicBrainz/Cover Art Archive 网络可达。

## 从源码构建

需要 .NET SDK 8.0 或更高版本：

```bash
dotnet build MusicStrmExtract/MusicStrmExtract.csproj -c Release
```

构建结果位于 `MusicStrmExtract/bin/Release/MusicStrmExtract.dll`。
