# MGA Sonic Anvil — macOS

Swift / AppKit 向けの置き場。**Xcode プロジェクトは未作成**（Mac 上で作成する）。

## 状態

- UI スタック: Swift（AppKit 中心）
- 進捗: 置き場のみ（[機能マトリクス](../../docs/mac/feature-matrix.md) 参照）
- Windows 版とは別バイナリ。同一リポジトリで追従する

## 進め方（Cursor）

- **同じ Git リポジトリを Mac の Cursor で開く**想定（Windows の Cursor と同じプロジェクト）
- ソース編集・エージェント作業は Cursor。ビルドは `xcodebuild` または Xcode
- Windows 上ではドキュメントと置き場の整備まで。Swift のビルドは Mac 必須

## Mac で最初にやること

1. Mac の Cursor でこのリポジトリを開く
2. このフォルダに Xcode の App プロジェクトを作成する（Bundle 名は後で Windows 製品名に合わせる）
3. 空ウィンドウ表示（マイルストーン M0）
4. 方針は [docs/mac/README.md](../../docs/mac/README.md)
