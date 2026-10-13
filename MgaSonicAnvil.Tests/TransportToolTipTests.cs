using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TransportToolTipTests
{
    [Theory]
    [InlineData("Space")]
    public void Play_IncludesSpace(string key)
    {
        Assert.Contains(key, UiStrings.TooltipPlay);
    }

    [Fact]
    public void Tooltips_IncludePrimaryShortcuts()
    {
        Assert.Contains("(I)", UiStrings.TooltipFadeIn);
        Assert.Contains("(O)", UiStrings.TooltipFadeOut);
        Assert.Contains("(N)", UiStrings.TooltipNormalize);
        Assert.Contains("Delete", UiStrings.TooltipDelete);
        Assert.Contains("Ctrl+S", UiStrings.TooltipSave);
        Assert.Contains("Ctrl+Shift+S", UiStrings.TooltipSaveAs);
        Assert.Contains("Ctrl+Shift+M", UiStrings.TooltipSaveMp3);
        Assert.Contains("Ctrl+R", UiStrings.TooltipRecord);
        Assert.Contains("(X)", UiStrings.TooltipFadeAround);
        Assert.Contains("(V)", UiStrings.TooltipVolume);
        Assert.Contains("(V)", UiStrings.TooltipLoudnessView);
        Assert.Contains("Esc", UiStrings.TooltipLoudnessView);
        Assert.Contains("Shift+A", UiStrings.TooltipLoudnessView);
        Assert.Contains("Shift+V", UiStrings.TooltipLoudnessView);
        Assert.Contains("(P)", UiStrings.TooltipPitch);
        Assert.Contains("(T)", UiStrings.TooltipTimeStretch);
        Assert.Contains("(R)", UiStrings.TooltipReverse);
        Assert.Contains("(M)", UiStrings.TooltipAddMarker);
        Assert.Contains("Shift+L", UiStrings.TooltipSetLoop);
        Assert.Contains("Shift+R", UiStrings.TooltipSetRegion);
        Assert.Contains("範囲メトロノーム (Q)", UiStrings.TooltipRangeClick);
        Assert.Contains("Ctrl+N", UiStrings.TooltipNew);
        Assert.Contains("Ctrl+O", UiStrings.TooltipOpen);
        Assert.Contains("Ctrl+Shift+O", UiStrings.TooltipSettings);
        Assert.Contains("(A)", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Esc", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Shift+A", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Shift+V", UiStrings.TooltipSpectrogramView);
        Assert.Contains("(Z)", UiStrings.TooltipCenterPlayhead);
        Assert.Contains("(U)", UiStrings.TooltipHistory);
        Assert.Contains("F10", UiStrings.TooltipLibraryMaximize);
        Assert.Contains("F10", UiStrings.TooltipLibraryMaximizeOff);
        Assert.Contains("F12", UiStrings.TooltipAnalyzerMaximize);
        Assert.Contains("F12", UiStrings.TooltipAnalyzerMaximizeOff);
        Assert.Contains("Ctrl+Shift+C", UiStrings.TooltipColorPanel);
        Assert.Contains("Alt+S", UiStrings.TipSilentSkip);
        Assert.Contains("Alt+A", UiStrings.TipAlwaysOnTop);
        Assert.Contains("Ctrl+Space", UiStrings.TipPlay);
        Assert.Contains("Enter でその場停止", UiStrings.TipPlay);
        Assert.Contains("停止中の動画／PDF は本再生／明るい表示", UiStrings.TipPlay);
        Assert.Contains("暗いプレビューへは Escape／Q", UiStrings.TipPlay);
        Assert.Contains("F8／引数起動ミニでは Escape／Q でアプリ終了", UiStrings.TipPlay);
        Assert.Contains("暗いプレビューへ戻す", UiStrings.TipRangeClick);
        Assert.Contains("F8／引数起動ミニでは Escape／Q でアプリ終了", UiStrings.TipRangeClick);
        Assert.Contains("範囲メトロノームはプレイヤーでは効かない", UiStrings.TipRangeClick);
        Assert.Contains("動画／PDF の本再生中やフレーム一時停止中", UiStrings.TipLibraryMaximizeOff);
        Assert.Contains("動画／PDF の本再生中やフレーム一時停止中の F10", UiStrings.TipWaveform);
        Assert.Contains("Ctrl+N", UiStrings.TipOpen);
        Assert.Contains("Ctrl+Shift+D", UiStrings.TipOpen);
        Assert.Contains("Ctrl+Shift+B", UiStrings.TipOpen);
        Assert.Contains("Esc", UiStrings.TipOpen);
        Assert.Contains("Esc", UiStrings.OverlayOpening);
        Assert.Contains("Shift+F10", UiStrings.TipWaveform);
        Assert.Contains("F10", UiStrings.TipWaveform);
        Assert.Contains("プレイリスト／ツリーの Tips", UiStrings.TipWaveform);
        Assert.Contains("マーカー／リージョン／ループは出さない", UiStrings.TipWaveform);
        Assert.Contains("ラウドネス", UiStrings.TipWaveform);
        Assert.Contains("*", UiStrings.TipLibraryExplorer);
        Assert.Contains("約3秒", UiStrings.TipLibraryExplorer);
        Assert.Contains("Ctrl+C", UiStrings.TipLibraryExplorer);
        Assert.Contains("エクスプローラーで開く", UiStrings.TipLibraryExplorer);
        Assert.Contains("ツリーのルートに登録", UiStrings.TipLibraryExplorer);
        Assert.Contains("赤字", UiStrings.TipLibraryExplorer);
        Assert.Contains("Shift+↑↓", UiStrings.TipLibraryExplorer);
        Assert.Contains("Shift+クリック", UiStrings.TipLibraryExplorer);
        Assert.Contains("Ctrl+C", UiStrings.TipLibraryFavorites);
        Assert.Contains("エクスプローラーで開く", UiStrings.TipLibraryFavorites);
        Assert.Contains("赤字", UiStrings.TipLibraryFavorites);
        Assert.Contains("Shift+↑↓", UiStrings.TipLibraryFavorites);
        Assert.Contains("Shift+クリック", UiStrings.TipLibraryFavorites);
        Assert.Contains("ダブルクリック", UiStrings.TipLibraryFavorites);
        Assert.Contains("プレイリストをクリアして追加", UiStrings.TipLibraryFavorites);
        Assert.Contains("プレイリストへ追加", UiStrings.TipLibraryFavorites);
        Assert.Contains("1曲ずつ", UiStrings.TipLibraryFavorites);
        Assert.Contains("Space ではプレイリストの再生を始めない", UiStrings.TipLibraryExplorer);
        Assert.Contains("Space ではプレイリストの再生を始めない", UiStrings.TipLibraryFavorites);
        Assert.Contains("プレイリストをアクティブにする", UiStrings.TipLibraryExplorer);
        Assert.Contains("プレイリストをアクティブにする", UiStrings.TipLibraryFavorites);
        Assert.Contains("見つけ次第再生", UiStrings.TipLibraryExplorer);
        Assert.Contains("動画／PDF は全件追加後", UiStrings.TipLibraryExplorer);
        Assert.Contains("先頭が音声なら見つけ次第", UiStrings.TipLibraryFavorites);
        Assert.Contains("動画／PDF は全件追加後", UiStrings.TipLibraryFavorites);
        Assert.Contains("Ctrl+C", UiStrings.TipLibraryList);
        Assert.Contains("エクスプローラーで開く", UiStrings.TipLibraryList);
        Assert.Contains("枠ごと隠す", UiStrings.TipLibraryList);
        Assert.DoesNotContain("No Image", UiStrings.TipLibraryList);
        Assert.Contains("行ごとには出さない", UiStrings.TipLibraryList);
        Assert.Contains("選択行のものを出す", UiStrings.TipLibraryList);
        Assert.Contains("動画／PDF はサムネイルがあれば各行に出す", UiStrings.TipLibraryList);
        Assert.Contains("画像が無いときのプレースホルダは出さない", UiStrings.TipLibraryList);
        Assert.Contains("PDF / MOV / MP4 / AVI / MKV / WebM / MPG", UiStrings.TipLibraryPlaylistDocuments);
        Assert.Contains("動画は既定オフ", UiStrings.TipLibraryPlaylistDocuments);
        Assert.Contains("1秒でフェードイン", UiStrings.TipLibraryList);
        Assert.Contains("即表示", UiStrings.TipLibraryList);
        Assert.Contains("最初から消す", UiStrings.TipLibraryList);
        Assert.Contains("1秒でフェードアウト", UiStrings.TipLibraryList);
        Assert.Contains("停止中の選択は波形だけ", UiStrings.TipLibraryList);
        Assert.Contains("空のときはライブラリ", UiStrings.TipLibraryList);
        Assert.Contains("Space またはトランスポートの再生はその曲だけ", UiStrings.TipLibraryList);
        Assert.Contains("シークバー位置から開始／再開", UiStrings.TipLibraryList);
        Assert.Contains("バックグラウンドへ戻すのは Escape／Q", UiStrings.TipLibraryList);
        Assert.Contains("前面 UI が隠れているときは Ctrl+A／Delete、およびリストにフォーカスがあるときのメニュー「プレイリストからクリア」では外さない", UiStrings.TipLibraryList);
        Assert.Contains("PDF の明るい表示モード中の抜けも Escape／Q", UiStrings.TipLibraryList);
        Assert.Contains("Enter はその場停止", UiStrings.TipLibraryList);
        Assert.Contains("停止中の動画／PDF は Space と同じく本再生／明るい表示へ", UiStrings.TipLibraryList);
        Assert.Contains("本再生中の動画はその場停止", UiStrings.TipLibraryList);
        Assert.Contains("ダブルクリックで再生し、終わったら次の曲へ進む", UiStrings.TipLibraryList);
        Assert.Contains("F8 で動画／PDF ミニプレイヤー（前面 UI 無し。背面に動画または PDF があるとき＝本再生または暗いバックグラウンドプレビュー、かつ再生対象があるとき", UiStrings.TipLibraryList);
        Assert.Contains("開始位置へ戻して静止する（即再開しない）", UiStrings.TipLibraryList);
        Assert.Contains("Escape／Q でアプリを終了する", UiStrings.TipLibraryList);
        Assert.DoesNotContain("抜けは Escape／Q", UiStrings.TipLibraryList);
        Assert.Contains("F 全画面中でも可", UiStrings.TipLibraryList);
        Assert.Contains("窓枠のドラッグは連続拡縮", UiStrings.TipLibraryList);
        Assert.Contains("25% 刻みではない", UiStrings.TipLibraryList);
        Assert.Contains("25% 刻み", UiStrings.TipLibraryList);
        Assert.Contains("+／-", UiStrings.TipLibraryList);
        Assert.Contains("F11／F12 へ移るとき動画／PDF はファイルごと持ち込まない", UiStrings.TipLibraryList);
        Assert.Contains("A／T の表示は F8 と引数起動で共通に覚え", UiStrings.TipLibraryList);
        Assert.Contains("プレイリストからの本再生は開始時オン", UiStrings.TipLibraryList);
        Assert.Contains("F9 でプレイリストと波形だけを出す", UiStrings.TipLibraryList);
        Assert.Contains("ステータスバーは出さない", UiStrings.TipLibraryList);
        Assert.Contains("Alt+A（Always on Top）", UiStrings.TipLibraryList);
        Assert.Contains("ギャップレス再生", UiStrings.TipLibraryList);
        Assert.Contains("iTunSMPB", UiStrings.TipGaplessPlayback);
        Assert.Contains("M4A", UiStrings.TipGaplessPlayback);
        Assert.Contains("F1 または Ctrl+←／↑", UiStrings.TipLibraryList);
        Assert.Contains("F3 または Ctrl+→", UiStrings.TipLibraryList);
        Assert.Contains("一時マーカーがあるとき Ctrl+←／→ は前／次のマーカーへ", UiStrings.TipLibraryList);
        Assert.Contains("Ctrl+Delete で選択または再生ヘッドのマーカーを削除", UiStrings.TipLibraryList);
        Assert.Contains("テンキーは再生操作", UiStrings.TipLibraryList);
        Assert.Contains("エディタでも同じ", UiStrings.TipLibraryList);
        Assert.Contains("上段 0–9 は割合ジャンプ", UiStrings.TipLibraryList);
        Assert.Contains("Shift＋上段数字は、一時マーカーがあるときマーカー番号へ", UiStrings.TipLibraryList);
        Assert.Contains("テンキーは使わない", UiStrings.TipLibraryList);
        Assert.Contains("二桁入力可", UiStrings.TipLibraryList);
        Assert.Contains("再生操作はどのペインからでも使える", UiStrings.TipLibraryList);
        Assert.Contains("テンキーは再生操作", UiStrings.TipLibraryExplorer);
        Assert.Contains("音声のプレイリスト波形と動画本再生／F8 では I／O で捨て A-B", UiStrings.TipWaveform);
        Assert.Contains("動画／PDF の暗いプレビューでは使わない", UiStrings.TipWaveform);
        Assert.Contains("ダブルクリック区間／Shift 追加／トリプル全選択はエディタ同様。コメント改名はしない", UiStrings.TipWaveform);
        Assert.Contains("Ctrl+↓", UiStrings.TipLibraryFavorites);
        Assert.Contains("ドラッグ", UiStrings.TipLibraryColumns);
        Assert.Contains("右クリック", UiStrings.TipLibraryColumns);
        Assert.Contains("20", UiStrings.TipClickGuardFade);
        Assert.Contains("Wwise.exe", UiStrings.TipWwiseProjectNameOpen);
        Assert.Contains("Ctrl+Alt+Shift+W", UiStrings.TipWwiseProjectNameOpen);
    }

    [Fact]
    public void GroupLabels_AreShortAndMatchImporter()
    {
        Assert.Equal("TRANS", UiStrings.LabelTransportGroup);
        Assert.Equal("EDIT", UiStrings.LabelEditGroup);
        Assert.Equal("MARK", UiStrings.LabelMarkerGroup);
        Assert.Equal("FILE", UiStrings.LabelFileGroup);
        Assert.Equal("VIEW", UiStrings.LabelViewGroup);
        Assert.Equal("HELP", UiStrings.LabelHelpGroup);
        foreach (var label in new[]
        {
            UiStrings.LabelTransportGroup,
            UiStrings.LabelEditGroup,
            UiStrings.LabelMarkerGroup,
            UiStrings.LabelFileGroup,
            UiStrings.LabelViewGroup,
            UiStrings.LabelHelpGroup,
        })
        {
            Assert.InRange(label.Length, 3, 5);
        }
    }

    [Fact]
    public void Tooltips_StayOnOneLine()
    {
        Assert.DoesNotContain("\n", UiStrings.TooltipPlay);
        Assert.DoesNotContain("\n", UiStrings.TooltipSave);
        Assert.DoesNotContain("\n", UiStrings.TooltipSaveMp3);
        Assert.DoesNotContain("\n", UiStrings.TooltipDelete);
        Assert.DoesNotContain("\n", UiStrings.TooltipColorPanel);
    }
}
