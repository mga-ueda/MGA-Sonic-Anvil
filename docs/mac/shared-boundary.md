# 共有境界（Swift Mac × Windows WPF）

Swift UI を選んだため、**C# の Editing / Domain をそのままリンクはできない**。  
当面の Windows 本線は C# のまま。共有は「仕様・スキーマ・後で切り出す核」から小さく取る。

## 原則

1. Windows のビルドと実行パスを壊さない。参照の差し替えは最小・段階的。
2. 先に **文書と契約（JSON・WAAPI の意味）** を揃え、コード共有は必要になってから。
3. UI・再生・描画・VST・映像は **共有しない**（各 OS で最良を取る）。

## 層ごとの方針

| 層 | 共有? | 今の置き場 | メモ |
|----|-------|------------|------|
| WPF UI / Win32 / DWM | しない | `UI\` | Mac は AppKit |
| 再生 I/O（WASAPI 等） | しない | `Audio\` | Mac は Core Audio |
| 映像 DXGI | しない | `Video\` | |
| VST3 ホスト | しない | `Audio\` | バイナリ形式が違う |
| 編集コマンド・履歴の**意味** | 将来 | `Editing\` | 最初は Windows 実装が正。移植時に仕様化 |
| ドメイン型・文言キーの**意味** | 将来 | `Domain\` | 文字列リソースは別管理でも可 |
| Wwise WAAPI の手順 | 候補 | `Wwise\` | HTTP JSON。Swift から再実装しやすい |
| settings のキー／形 | 候補 | `Config\AppSettings.cs` | 互換を破る変更はメモ必須 |
| DSP（Signalsmith / EffeTune） | 候補 | `native\` | C API があれば両 OS から呼べる |

## Windows を止めない進め方

- 機能追加・バグ修正は **現行の WPF プロジェクトに直接**入れてよい。
- Mac 未追従の機能は [feature-matrix.md](feature-matrix.md) の Mac 列を `なし` のまま残す。
- C# から型を「共有ライブラリへ移動」するのは、**テスト付き・参照切れなし**が確認できてから。急ぎの Mac 作業より Windows 安定を優先。

## 将来のコード共有オプション（まだ採らない）

必要になったら、コスト順で見直す。

1. **仕様共有のみ**（現状の想定）— Swift 側に同意味の実装を書く  
2. **native C API** — 編集核や DSP だけ C/C++ 化し、両 OS から呼ぶ  
3. **埋め込み .NET** — 橋が重いので最後の手段

いまは 1。2 に進む判断は、編集ロジックの二重管理が明らかに痛くなってから。

## 抽出・移植の推奨順（参考）

Windows 非破壊のまま「正」を明確にする順。

1. WAAPI の成功パス（Wave 単体 EXPORT）の手順メモ → [waapi-wave-only.md](waapi-wave-only.md)  
2. マーカー／ループ／リージョンの永続・意味  
3. 編集履歴に載る操作の一覧（ユーザー文言は Bounce 等に既に揃えつつある）  
4. DSP の C 境界（`native/` のヘッダがそのまま使えるか）
