# Music Strm Extract

Music Strm Extract 是一个 Emby 插件，给音乐库里的 `.strm` 音频补全 MusicBrainz 元数据。`.strm` 本身只是远程播放链接，没有可读取的音乐标签，插件会根据你的目录结构和文件名轨号，自动找到匹配的专辑和歌曲，补上标题、歌手、专辑、年份、碟号、轨号、MusicBrainz ID 和封面。

## 什么时候用

- 你的音乐库以 `.strm` 文件为主，播放时才指向远程地址。
- 目录已经按 `歌手 / 专辑 / 曲目` 组织，但 Emby 里显示的还是文件名。
- 希望自动从 MusicBrainz 拉取官方元数据，不需要手动逐首填写。
- 不想让插件检查远程文件是否能播放，也不需要读取 `.strm` 内容。

## 安装

1. 从 [Releases](https://github.com/AIerlIz/MusicStrmExtract/releases) 下载 `MusicStrmExtract.dll`。
2. 把 DLL 复制到 Emby 的插件目录。

   Windows 示例：

   ```text
   C:\Users\<你的用户名>\AppData\Roaming\Emby-Server\programdata\plugins\
   ```

   Docker/Linux 请放进 Emby 对应的 `plugins` 目录。

3. 重启 Emby Server。
4. 打开 Emby 管理后台，在“服务器 → 插件”里确认出现 “Music Strm Extract”。

插件没有外部 DLL 依赖，复制主 DLL 即可。

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

- 文件名必须以轨号开头，例如 `01 - `、`02 - `，轨号要和 MusicBrainz 的 track number 对应。
- 文件名里的标题部分不参与匹配，可以简写、省略或写错，只要轨号正确即可。
- 专辑文件夹名可以带年份，例如 `叶惠美 (2003)`、`七里香-2004`。
- 多碟可以使用 `Disc 1`、`CD2` 子目录，也可以在专辑目录平铺，用 `1-01 - `、`CD1-01 - `、`01.01 - ` 这类碟号+轨号开头。
- 评论轨保留 `(Commentary)`、`Commentary` 或 `评论轨` 后缀即可。评论轨和正式轨奇偶交错排列（`01/03/05` 评论 + `02/04/06` 正式轨）也能自动归一化。

不符合以上结构时，插件会跳过，不修改该条目的元数据。

## 插件会自动做什么

- 扫描或刷新时，根据“歌手文件夹 + 专辑文件夹”锁定 MusicBrainz release。
- 整张专辑只查询一次，之后 30 分钟内命中缓存，不重复请求 MusicBrainz。
- 按轨号从官方 tracklist 取回标题、歌手、专辑、年份、轨号、碟号和 MusicBrainz IDs。
- 评论轨沿用官方曲名，并在标题后保留 `(Commentary)`。
- 候选版本优先匹配“本地轨数与 MusicBrainz media 轨数完全一致”的版本；没有完全一致时，退回轨号覆盖匹配，避免普通版被豪华版抢走。
- 自动建立或修正 Audio、MusicAlbum、MusicArtist 的归属关系。
- 封面由 Emby 从 Cover Art Archive 下载，不需要手写 `cover.jpg`。

## 配置

1. 打开 Emby 管理后台，进入“服务器 → 插件”。
2. 在 “Music Strm Extract” 上点击“设置”。
3. 修改配置后保存，不需要重启 Emby。

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `MusicBrainzBaseUrl` | 空 | MusicBrainz 服务地址。留空使用官方 `https://musicbrainz.org`；官方不稳定时可填写镜像，例如 `https://musicbrainz.emby.tv` |

## 首次使用

保持目录结构后，执行一次媒体库扫描，或对音频条目执行“刷新元数据”。之后在 Emby 中打开 Audio 或 MusicAlbum，检查标题、歌手、专辑、年份、封面和 MusicBrainz 信息是否已出现。

如果刷新前 Emby 里已经存在同名 MusicAlbum，旧的专辑归属关系可能不会自动重建。删除旧的 MusicAlbum 后重新扫描媒体库即可。

## 常见问题

**MusicBrainz 连接不稳定怎么办？**

在插件设置里填写可用的镜像地址。MusicBrainz 不可达时插件不会用其它来源补全，也不会写入未经验证的脏元数据。

**为什么某些 `.strm` 没有补全？**

常见原因：

- 不满足 `歌手 / 专辑 / 曲目` 两层结构，例如 `.strm` 直接放在歌手目录下。
- 文件名没有数字轨号，或轨号在 MusicBrainz tracklist 中不存在。
- 本地把 bonus 单独分碟，但 MusicBrainz 把该版本建模成单张合并 tracklist，碟号对不上独立 media。
- 文件名带的是 `(Live)`、`(Demo)`、`(Acoustic)` 等其它变体后缀，目前插件只自动识别评论轨。
- 目录所在的专辑在 MusicBrainz 上没有可匹配的 release。

**评论轨和正式轨轨号重复正常吗？**

正常。评论轨与正式轨共用官方轨号，靠标题中的 `(Commentary)` 区分，例如专辑里会出现 `01 - Welcome to New York` 和 `01 - Welcome to New York (Commentary)`。

**为什么选到了豪华版而不是标准版？**

插件会先看轨数是否完全一致。如果本地就是豪华版，会选豪华版；如果本地是标准版但候选里没有轨数一致的标准版，才会退回“轨号能覆盖”的版本。

**远程文件打不开会影响匹配吗？**

不会。插件不读取 `.strm` 内容，也不探测远程文件是否可播放。
