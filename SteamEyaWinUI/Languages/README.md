# 界面语言 / UI Languages

本目录下每个 `<code>.json` 是一种界面语言。应用启动时会自动发现并加载：

1. 程序内嵌的语言包（保底）；
2. 程序目录下的 `Languages\*.json`（即本目录，随发布一起分发）；
3. `%AppData%\SteamEYA\Languages\*.json`（用户可自行拖入新语言，无需重新编译）。

后者覆盖前者中相同 `code` 的语言，方便本地试译。

## 贡献 / 新增一种语言

1. 复制 `en.json` 为 `<你的语言代码>.json`（如 `ja.json`、`fr.json`、`ko.json`，代码用 BCP-47，如 `pt-BR`）。
2. 修改文件头：
   - `code`：与文件名一致的语言代码；
   - `name`：该语言的**自称名**（用该语言本身书写，如 `日本語` / `Français`），它会显示在设置页的语言列表里；
   - `fallback`：缺某个键时回退到哪种语言（一般保留 `"zh-Hans"`）。
3. 翻译 `strings` 里每个键的值。**键名不要改、不要删**；含 `{0}`、`{1}` 的占位符要原样保留（顺序和数量一致）。
4. 放回本目录（贡献到仓库）或丢到 `%AppData%\SteamEYA\Languages\`（仅本机生效），重启应用即可在「设置 → 语言」中看到。

缺失的键会自动回退到 `fallback`（默认简体中文），所以翻译可以分批进行，未译的键不会显示为空。

简体中文 `zh-Hans.json` 是源语言，键最全；以它为准对照翻译。

---

Each `<code>.json` here is one UI language. To add a language: copy `en.json` to `<code>.json`, set `code`/`name`/`fallback` in the header, translate the values under `strings` (keep the keys and `{0}`/`{1}` placeholders unchanged), then drop it in this folder (or in `%AppData%\SteamEYA\Languages\`) and restart. Missing keys fall back to `fallback` (default `zh-Hans`).
