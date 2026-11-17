# Wave 単体 EXPORT（WAAPI）手順メモ

Windows 実装の正: `Wwise/WaveOnlyWaapiImporter.cs` ほか。  
Mac 再実装時の契約メモ。API 詳細はコードを優先する。

## 前提

- Wwise Authoring が WAAPI（HTTP）で待ち受けている
- 作成先（親）パスが決まっている
- 入力は編集後の波形ドキュメント 1 本

## 成功パス（概要）

1. **プラン作成**（`WaveOnlyPlanBuilder`）  
   マーカー／リージョン／サンプルループから -A / -L / -E 等の区間と Music Playlist Container 名を決める。

2. **WAV は分割しない**  
   全体を 1 ファイルで Originals 側ディレクトリへ書く（元ファイル名ベース、拡張子 `.wav`）。

3. **`ak.wwise.core.object.set`**  
   Music Playlist Container と配下の Music Segment／クリップ構造を一括作成。  
   各クリップは同一 WAV を参照する。

4. **Segment の Entry / Exit Cue**  
   タイプ 0 = Entry、1 = Exit。Custom Cue にはしない。

5. **クリップの Trim**  
   Begin/End Trim Offset で区間を合わせる。  
   負の PlayAt が必要な場合は WAAPI 制約のため **WWU 直接編集**で補正（Windows 実装と同じ制約）。

6. **Play post-exit（任意）**  
   Play -E 相当。Playlist の PlaySourcePostExit も WWU パッチ（WAAPI 非対応な部分あり）。

7. **選択**  
   作成したコンテナを Wwise UI 上で選択（失敗しても致命ではない実装あり）。

## 共有しやすさ

- URI 一覧: `Wwise/WaapiUris.cs`
- HTTP クライアント: `Wwise/WaapiHttpClient.cs`
- OS 非依存なのは **JSON-RPC の意味と順序**。UI やファイルピッカーは各 OS。

## Mac での扱い

機能マトリクス上は当面 **なし**。実装するときは本メモと Windows コードを突き合わせ、差分が出たら Windows 側のコメントか本ファイルを更新する。
