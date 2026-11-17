# Mac 版（Swift / AppKit）並行開発

Windows 版（このリポジトリの現行アプリ）を本線のまま維持しつつ、Mac 版をゆっくり足す。  
UI スタックは **Swift（AppKit 中心）**。見た目と性能は OS ごとに最適化する。

## 制約（はずさない）

- **Windows の動作保証は必須。** いつでも Windows のバグ修正・機能追加が入る前提。
- Mac は進捗が大きく遅れてもよい（画面だけ → 音だけ、など）。
- 巨大な共通 UI 基盤や、Windows コードの一括移動はしない。

## リポジトリ配置

| 場所 | 役割 |
|------|------|
| リポジトリ直下（現行） | Windows 版（.NET 8 + WPF）。変更の本線 |
| `apps/macos/` | Mac 版（Xcode / Swift）。未着手の置き場 |
| `docs/mac/` | 方針・機能マトリクス・共有境界 |
| `native/` | DSP など。将来 `mac-*` バイナリを並べる想定 |

Windows ツリーを `apps/windows` へ移す作業は、必要になるまで行わない（移動リスクを避ける）。

## 開発環境（Cursor 前提）

Mac 版も **Cursor で同じリポジトリを開いて**進める想定。  
Windows 用 Cursor と Mac 用 Cursor でブランチ／作業を分ける必要はなく、モノレポのまま切り替えてよい。

| 作業 | どこで |
|------|--------|
| Windows の修正・機能追加、ドキュメント、共有境界の整理 | **Windows 上の Cursor で可** |
| `apps/macos` の Swift 編集、`xcodebuild`、実機／シミュレータ確認 | **Mac 上の Cursor**（＋ Xcode コマンドラインツール） |
| 署名・公証・一部の Xcode 専用操作 | **Mac**（Cursor からターミナル／必要なら Xcode アプリ） |

ビルド・リンク・公証は Mac のツールチェインが必須。  
Windows 上の Cursor では Swift のコンパイルまでは行わない。Mac 作業に入る前に、こちらから切り替えを明示する。

## バージョン方針

- 製品の「最新」は当面 **Windows**。
- Mac は別ペース（初期は `0.x` や「プレビュー／一部機能」表記を想定）。
- 詳細な対応状況は [feature-matrix.md](feature-matrix.md)。

## 次にやること（短い列）

1. 本ドキュメント群で方針を固定する（済みの土台）
2. Windows は従来どおり開発を続ける
3. Mac 実作業の最初は、Mac 上で `apps/macos` に Xcode プロジェクトを作り「空ウィンドウ」まで
4. 共有は [shared-boundary.md](shared-boundary.md) の順に、小さく・Windows 非破壊で

関連: [feature-matrix.md](feature-matrix.md) · [shared-boundary.md](shared-boundary.md) · [waapi-wave-only.md](waapi-wave-only.md) · [apps/macos/README.md](../../apps/macos/README.md)
