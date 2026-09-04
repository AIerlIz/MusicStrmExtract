# Music Strm Extract

Music Strm Extract 是一个 Emby 插件。它根据音乐库的目录结构和文件名轨号，从 MusicBrainz 找到对应专辑和歌曲，为 `.strm` 音乐补全元数据。

## 功能

- 识别 `歌手 / 专辑 / 序号 - 标题.strm` 这类两层目录结构，自动锁定 MusicBrainz 专辑。
- 按轨号从 MusicBrainz 官方 tracklist 取回每首歌的标题、歌手、专辑、年份和 MBID。
- 自动识别 `(Commentary)`/`Commentary`/`评论轨` 评论轨，支持奇偶交错、同轨号、评论轨在前/在后等常见布局；评论轨使用官方曲名并保留 `(Commentary)` 后缀。
- 候选 release 先按“本地碟轨数与 MusicBrainz media 轨数完全一致”优先，再退回仅轨号覆盖，避免标准版被豪华版/加歌版抢先命中。
- 不要求 `.strm` 指向的远程文件可播放。
- 扫描或刷新元数据时自动工作，不需要手动运行任务。
- 同专辑结果缓存 30 分钟，避免重复刷新时反复请求 MusicBrainz。
- 封面交给 Emby 从 Cover Art Archive 下载，不需要手写 `cover.jpg`。

## 目录结构

插件按下面这种两层结构工作:单碟时 `.strm` 直接在专辑目录下:

```text
音乐库/
  周杰伦/
    叶惠美 (2003)/
      01 - 以父之名.flac.strm
      02 - 懦夫.flac.strm
      ...
    七里香 (2004)/
      01 - 我的地盘.flac.strm
      02 - 七里香.flac.strm
  ...
```

多碟专辑可以使用 `Disc N`/`CD N` 子目录,每个子目录对应一张碟:

```text
音乐库/
  Taylor Swift/
    Midnights (2022)/
      Disc 1/
        01 - Lavender Haze.m4a.strm
        02 - Maroon.m4a.strm
        ...
      Disc 2/
        01 - You're Losing Me.m4a.strm
      Disc 3/
        01 - Hits Different.m4a.strm
```

要点：

- 第一层是歌手文件夹，第二层是专辑文件夹，`.strm` 直接放在专辑文件夹里。
- 多碟时第二层是专辑文件夹，第三层是 `Disc 1`/`CD2` 等碟目录，`.strm` 放在碟目录里。
- 文件名以轨号开头，例如 `01 - `，轨号应当和 MusicBrainz 专辑的 track number 一致。
- 评论轨只需保留 `(Commentary)`/`评论轨` 等后缀；如果是 `01/03/05` 评论 + `02/04/06` 正式轨的交错布局，插件会先把评论轨归一到官方轨号再匹配，不会误判为 26 轨专辑。
- 在专辑目录平铺时也支持 `1-01 - `、`CD1-01 - `、`01.01 - ` 这类碟号+轨号文件名。
- 一张专辑内的轨号建议连续且不重复。
- 文件名里的标题部分不会参与匹配，可以简写或省略。
- 专辑文件夹名带年份也可以，例如 `叶惠美 (2003)`、`七里香-2004`。

如果 `.strm` 不符合这个结构，插件不会为它补全元数据。

## 安装

1. 把 `MusicStrmExtract.dll` 复制到 Emby 的插件目录。

   Windows 示例：

   ```text
   C:\Users\<用户名>\AppData\Roaming\Emby-Server\programdata\plugins\
   ```

   Docker/Linux 安装请放到 Emby 对应的 `plugins` 目录。

2. 重启 Emby Server。
3. 在插件列表中确认出现 “Music Strm Extract”。

插件没有外部 DLL 依赖，只复制主 DLL 即可。

## 使用方式

插件会在以下时机自动处理：

- 新 `.strm` 文件入库扫描；
- 媒体库扫描；
- 手动对音频条目执行“刷新元数据”。

不需要创建计划任务，也不需要手动运行后台任务。

要验证效果，保持上面的目录结构，执行一次媒体库扫描或刷新音频元数据，然后打开 Audio 或 MusicAlbum，检查标题、歌手、专辑、年份、封面和 MusicBrainz 信息是否已经出现。

如果刷新前 Emby 里已经存在同名 MusicAlbum，旧的专辑归属关系可能不会自动重建，可以删除对应 MusicAlbum 后重新扫描媒体库。

## 配置

1. 打开 Emby 管理后台，进入“服务器 → 插件”。
2. 在 “Music Strm Extract” 上点击“设置”。
3. 按需修改配置并保存，保存后无需重启 Emby。

设置页包含以下配置项：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `MusicBrainzBaseUrl` | 空 | MusicBrainz 服务地址。留空使用官方 `https://musicbrainz.org`；官方不稳定时可填写镜像，例如 `https://musicbrainz.emby.tv` |

## 注意事项

- 插件只依赖目录结构和文件名轨号，不读取 `.strm` 文件内容，也不要求远程文件可播放。
- 插件只使用 MusicBrainz 作为在线数据源；MusicBrainz 不可达时不会从其它来源补全。
- 官方 MusicBrainz 在部分地区可能不稳定，建议配置稳定的镜像或代理。
- 多碟目录按碟号对应 MusicBrainz 的 media 位置，适合 MB 数据本身就是多碟（2xCD、3xLP 等）的专辑。
- 如果本地把 bonus 曲单独分了碟，但 MusicBrainz 把该版本建模成单张合并 tracklist（例如 Taylor Swift《Midnights》），碟号无法对应到独立 media，这类专辑仍可能无法命中。
- 封面来自 Cover Art Archive，需要 MusicBrainz/Cover Art Archive 网络可达。
