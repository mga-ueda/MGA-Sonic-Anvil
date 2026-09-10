# MGA Sonic Anvil

[English](README.en.md)

ゲーム向け波形を、聴いて切って、マーカーとループを置いて、Wwise へ Wave 単体で渡す。

## このアプリの魅力

**波形エディタと、Wwise Interactive Music への Wave 単体 EXPORT をひとつにしたもの**です。

フェード・ノーマライズ・部分削除・コピー／ペースト、マーカー／リージョン／サンプルループ、スペクトログラムまで、聴きながら決めて焼き込めます。DAW や専用エディタを往復しなくても、ゲーム用ワンショットとループの下ごしらえがこのウィンドウで完結します。

**Wwise へは Wave 単体モードで渡せます。** レベルメーター上の WAAPI をオンにし、作成先を選んで EXPORT すると、元波形を Originals へ書き、Music Playlist Container（マーカー／サンプルループ。Custom Cue は出しません）として取り込みます。Play -E はループ折り返しのプレビューと、EXPORT 時の Play post-exit に使います。

**MP3 にも書けます。** 設定の LAME パスが有効ならユーザー用意の `lame.exe`、空欄または無効なら Windows（既定 192 kbps）。タブの右クリックで、今の編集内容を Wave / MP3 として書き出せます（複数タブはフォルダ指定。未保存のままで可）。

**表示言語は日本語／英語です。** 日本語版の画面文言は、英語のままのラベルも含め現状どおりです。英語版では日本語だけを訳します。切替は左上の歯車（設定）。既定は **Auto**（OS が日本語なら Japanese、それ以外は English）。

## マニュアル・ダウンロード

- マニュアル: [日本語](https://mga-ueda.github.io/MGA-Sonic-Anvil/manual.ja.html) · [English](https://mga-ueda.github.io/MGA-Sonic-Anvil/manual.en.html) · [一覧](https://mga-ueda.github.io/MGA-Sonic-Anvil/)
- 初めての方: [クイックスタート](https://mga-ueda.github.io/MGA-Sonic-Anvil/manual.ja.html#quickstart)
- アプリ内: トランスポートの **マニュアル（`?`）**（表示言語に追従）
- 配布: [Releases](https://github.com/mga-ueda/MGA-Sonic-Anvil/releases)
- 設定データ: `%LocalAppData%\MGA\MGA Sonic Anvil\`（`settings.json`。exe 横には書きません）
- 起動: 二重起動しません。2 回目は既存ウィンドウを前面にし、渡したファイルを開きます
- 起動時に GitHub Releases を見て、新しい版があれば知らせます（自動ダウンロードはしません）
- ライセンス: [MIT](LICENSE)

Wwise®／Audiokinetic® は各権利者の商標です。本ツールは非公式です。

LAME は LAME project の名称で、ライセンスは LGPL です。同梱・改変・リンク・再配布せず、ユーザーが用意した `lame.exe` をプロセスとして呼び出すだけです。入手とライセンス遵守は利用者側です。LAME プロジェクトの非公式です。

Signalsmith Stretch（Signalsmith Audio、MIT）をピッチシフトに同梱しています。
