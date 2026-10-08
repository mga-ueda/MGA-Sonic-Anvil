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
        Assert.Contains("(K)", UiStrings.TooltipRangeClick);
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
        Assert.Contains("プレイヤーでは Space／Enter", UiStrings.TipPlay);
        Assert.Contains("プレイリストの Tips", UiStrings.TipPlay);
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
        Assert.Contains("Shift+↑↓", UiStrings.TipLibraryExplorer);
        Assert.Contains("Shift+クリック", UiStrings.TipLibraryExplorer);
        Assert.Contains("Ctrl+C", UiStrings.TipLibraryFavorites);
        Assert.Contains("エクスプローラーで開く", UiStrings.TipLibraryFavorites);
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
        Assert.Contains("Space はその曲だけ", UiStrings.TipLibraryList);
        Assert.Contains("トランスポートの再生", UiStrings.TipLibraryList);
        Assert.Contains("終わったら次の曲へ進む", UiStrings.TipLibraryList);
        Assert.Contains("F8 で動画専用ミニプレイヤー（前面 UI 無し。本再生中かつ再生対象があるときだけ", UiStrings.TipLibraryList);
        Assert.Contains("Space／Enter で止めると止めた位置で静止", UiStrings.TipLibraryList);
        Assert.Contains("F 全画面中でも可", UiStrings.TipLibraryList);
        Assert.Contains("F11／F12 へ移るとき動画／PDF はファイルごと持ち込まない", UiStrings.TipLibraryList);
        Assert.Contains("A／T の表示は F8 と引数起動で共通に覚え", UiStrings.TipLibraryList);
        Assert.Contains("プレイリストからの本再生は開始時オン", UiStrings.TipLibraryList);
        Assert.Contains("F9 でプレイリストと波形だけを出す", UiStrings.TipLibraryList);
        Assert.Contains("ステータスバーは出さない", UiStrings.TipLibraryList);
        Assert.Contains("Alt+A（Always on Top）", UiStrings.TipLibraryList);
        Assert.Contains("ギャップレス再生", UiStrings.TipLibraryList);
        Assert.Contains("iTunSMPB", UiStrings.TipGaplessPlayback);
        Assert.Contains("M4A", UiStrings.TipGaplessPlayback);
        Assert.Contains("F1 または Ctrl+←／Ctrl+↑", UiStrings.TipLibraryList);
        Assert.Contains("Ctrl+→", UiStrings.TipLibraryList);
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
