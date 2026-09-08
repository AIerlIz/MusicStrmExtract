# Music Strm Extract

Music Strm Extract 是 Emby 的本地元数据读取器插件，专门为音乐库里的 `.strm` 音频补全 MusicBrainz 元数据。

`.strm` 只是指向远程播放地址的文本文件，没有可读取的音乐标签。插件根据 `歌手 / 专辑 / 曲目` 目录结构和文件名轨号，从 MusicBrainz 找到对应专辑与轨道，把标题、歌手、专辑、年份、碟号、轨号和 MusicBrainz ID 返回给 Emby，由 Emby 负责合并保存与组织。

## 使用边界

- 插件只处理 `.strm` 音频，普通音频文件不进入本插件链路。
- 插件实现的是本地元数据读取器，不是 Emby 的在线元数据下载器，也不提供在线图片 Provider。
- 插件不直接写媒体库；Emby 拿到 `MetadataResult<Audio>` 后自行创建/复用 MusicAlbum、MusicArtist 并落库。
- 封面由 Emby 内置的 MusicBrainz 图像获取器从 Cover Art Archive 拉取，插件不参与封面下载。
- 选版只使用 MusicBrainz release 元数据，不使用 Cover Art 封面数量做同分决胜。

## 什么时候用

- 音乐库以 `.strm` 文件为主，播放时才指向远程地址。
- 目录已经按 `歌手 / 专辑 / 曲目` 组织，但 Emby 里显示的仍是文件名。
- 希望自动补全官方 MusicBrainz 元数据，不需要手动逐首填写。
- 不要求插件读取 `.strm` 内容，也不要求探测远程文件是否可播放。

## 安装

1. 从 [Releases](https://github.com/AIerlIz/MusicStrmExtract/releases) 下载 `MusicStrmExtract.dll`。
2. 把 DLL 复制到 Emby 插件目录。

   Windows 示例：

   ```text
   C:\Users\<你的用户名>\AppData\Roaming\Emby-Server\programdata\plugins\
   ```

   Docker/Linux 请放进 Emby 对应的 `plugins` 目录。

3. 重启 Emby Server。
4. 打开 Emby 管理后台，在“服务器 → 插件”里确认出现 `Music Strm Extract`。

插件没有外部 DLL 依赖，复制主 DLL 即可。

## 媒体库设置

插件通过“元数据读取器”工作，不需要出现在“元数据下载器”里：

- 打开音乐库的媒体库设置，在“元数据读取器”中保留 `Music Strm Extract` 并保持启用；禁用后 `.strm` 不再补全。
- “元数据下载器”不需要勾选本插件；旧版本残留的 `Music Strm Extract (在线)` 可以直接删除。
- 封面由 Emby 内置 MusicBrainz 图像获取器处理。MusicAlbum 的图片获取器请保留 `MusicBrainz`；若 Emby 无法访问 Cover Art Archive，封面不会出现。

## 目录结构

插件只处理两层结构：第一层是歌手文件夹，第二层是专辑文件夹，`.strm` 文件放在专辑文件夹或 `Disc N` 子目录里。

单碟专辑：

```text
音乐库/
  周杰伦/
    叶惠美 (2003)/
      01 - 以父之名.flac.strm
      02 - 懦夫.flac.strm
      ...
```

多碟专辑：

```text
音乐库/
  Taylor Swift/
    Midnights (2022)/
      Disc 1/
        01 - Lavender Haze.m4a.strm
        02 - Maroon.m4a.strm
      Disc 2/
        01 - You're Losing Me.m4a.strm
      Disc 3/
        01 - Hits Different.m4a.strm
```

评论轨和正式轨可以放在同一个专辑目录：

```text
音乐库/
  Taylor Swift/
    1989 (2014)/
      01 - Welcome to New York (Commentary).flac.strm
      02 - Welcome To New York.flac.strm
      03 - Blank Space (Commentary).flac.strm
      ...
```

### 命名要点

- 文件名必须以轨号开头，例如 `01 - `、`02 - `，轨号要与 MusicBrainz track number 对应。
- 文件名里的标题部分不参与匹配，可以简写、省略或写错，只要轨号正确即可。
- 专辑文件夹名可以带发行年份，例如 `叶惠美 (2003)`、`七里香-2004`。
- 多碟可以使用 `Disc 1`、`CD2` 子目录，也可以平铺用 `1-01 - `、`CD1-01 - `、`01.01 - ` 这类碟号+轨号开头。
- 评论轨保留 `(Commentary)`、`Commentary` 或 `评论轨` 后缀；完整奇偶交错排列（`01/03/05` 评论 + `02/04/06` 正式轨）也能自动归一化。

不符合以上结构时，插件会跳过，不修改该条目的元数据。

## 插件工作流程

- Emby 扫描或刷新 Audio 条目时，以本地元数据读取器身份调用插件；非 `.strm` 路径直接返回空结果。
- 插件根据歌手文件夹与专辑文件夹在 MusicBrainz 定位 release-group（专辑概念），再从该组的 release（可购买发行版本）中挑选匹配版本。
- 候选优先找“本地轨数与 MusicBrainz media 轨数逐碟完全一致，且本地覆盖 release 全部 media”的版本；没有 exact 时退回轨号覆盖匹配，避免普通版被豪华版抢走。
- 多国家/多介质候选按状态、年份贴近、国家、完整日期、条码、CD 介质和歧义描述分层；残余同分按日期、质量分与稳定次序决定，不请求 Cover Art。
- 整张专辑一次定位后缓存 30 分钟，同专辑后续 `.strm` 不再重复查询 MusicBrainz。
- 按轨号从官方 tracklist 取回 recording MBID、标题与艺人，连同 release/album-artist/release-group MBID 一起返回给 Emby。
- 评论轨沿用官方曲名并保留 `(Commentary)` 后缀。
- Emby 收到结果后负责保存并建立 Audio、MusicAlbum、MusicArtist 关联；封面随后由 Emby 内置 MusicBrainz 图像获取器完成。

## 配置

1. 打开 Emby 管理后台，进入“服务器 → 插件”。
2. 在 `Music Strm Extract` 上点击“设置”。
3. 修改配置后保存，不需要重启 Emby。

设置页还提供“运行旧库修复”按钮，用于删除未被 Audio 引用且缺少 MusicBrainzAlbum 的陈旧 MusicAlbum，并刷新相关 `.strm`。

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `MusicBrainzBaseUrl` | 空 | MusicBrainz 服务地址。留空使用官方 `https://musicbrainz.org`；官方不稳定时可填写镜像，例如 `https://musicbrainz.emby.tv` |

## 首次使用

保持目录结构后，执行一次媒体库扫描，或对音频条目执行“刷新元数据”。之后检查 Audio 或 MusicAlbum 的标题、歌手、专辑、年份与 MusicBrainz 信息。

若库里已经存在旧的同名 MusicAlbum，归属关系可能不会自动重建。删除旧的 MusicAlbum 后重新扫描即可。

## 常见问题

**为什么有些 `.strm` 没有补全？**

常见原因：

- 不满足 `歌手 / 专辑 / 曲目` 两层结构，例如 `.strm` 直接放在歌手目录下。
- 文件名没有数字轨号，或轨号在 MusicBrainz tracklist 中不存在。
- 本地把 bonus 单独分碟，但 MusicBrainz 把该版本建模成单张合并 tracklist，碟号对不上独立 media。
- 文件名带的是 `(Live)`、`(Demo)`、`(Acoustic)` 等其它变体后缀，目前插件只识别评论轨。
- 目录对应的专辑在 MusicBrainz 上没有可匹配的 release。
- 媒体库的“元数据读取器”里禁用了 `Music Strm Extract`。

**MusicBrainz 连接不稳定怎么办？**

在插件设置里填写可用的镜像地址。MusicBrainz 不可达时插件不会用其它来源补全，也不会写入未经验证的脏元数据。

**封面没有出现？**

封面由 Emby 内置 MusicBrainz 图像获取器完成，插件不参与。请确认 MusicAlbum 图片获取器启用了 `MusicBrainz`，且 Emby 能访问 Cover Art Archive。

**为什么选到了豪华版而不是标准版？**

插件先按本地轨数找完全一致的版本。如果本地就是豪华版，会选豪华版；如果本地是标准版但候选里没有轨数一致的标准版，才会退回“轨号能覆盖”的版本。

**评论轨和正式轨轨号重复正常吗？**

正常。评论轨与正式轨共用官方轨号，靠标题中的 `(Commentary)` 区分。

**远程文件打不开会影响匹配吗？**

不会。插件不读取 `.strm` 内容，也不探测远程文件是否可播放。
