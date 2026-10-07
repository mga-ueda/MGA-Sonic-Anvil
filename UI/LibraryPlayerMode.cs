using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>F10 プレイヤーモードで拒否する編集系コマンドと、リストから通すキー。</summary>
internal static class LibraryPlayerMode
{
    /// <summary>プレイヤーでも波形メニューを出す（編集系は <see cref="BlocksWaveMenu"/> で拒否）。</summary>
#pragma warning disable IDE0060 // 呼び出し側の名前付き引数 playerMode を残す
    public static bool HidesWaveformContextMenu(bool playerMode) => false;
#pragma warning restore IDE0060

    /// <summary>プレイヤーではマーカー／リージョン／ループを波形に出さない。</summary>
    public static bool ShowsCueOverlays(bool playerMode) => !playerMode;

    /// <summary>
    /// 波形の再生ヘッド（シークバー）とマウス直下ガイド。
    /// プレイヤーの PDF はタイムラインが無いので出さない。
    /// </summary>
    public static bool ShowsSeekCursors(bool playerMode, bool isPdf) => !playerMode || !isPdf;

    public static bool BlocksWaveMenu(WaveMenuCommand command) => command switch
    {
        WaveMenuCommand.PlayFromHere
            or WaveMenuCommand.SeekHere
            or WaveMenuCommand.SelectSpanHere
            or WaveMenuCommand.SelectAll
            or WaveMenuCommand.ClearSelection
            or WaveMenuCommand.SelectToStart
            or WaveMenuCommand.SelectToEnd
            or WaveMenuCommand.TogglePlayback
            or WaveMenuCommand.PauseHere
            or WaveMenuCommand.Preroll
            or WaveMenuCommand.Restart
            or WaveMenuCommand.LoopPlay
            or WaveMenuCommand.Shuffle
            or WaveMenuCommand.GoStart
            or WaveMenuCommand.GoEnd
            or WaveMenuCommand.ViewLeft
            or WaveMenuCommand.ViewRight
            or WaveMenuCommand.MaximizeWaveform
            or WaveMenuCommand.MaximizeAnalyzers
            or WaveMenuCommand.MaximizeLibrary
            or WaveMenuCommand.ViewWaveform
            or WaveMenuCommand.SilentSkip
            or WaveMenuCommand.AlwaysOnTop
            or WaveMenuCommand.Open
            or WaveMenuCommand.Settings
            or WaveMenuCommand.ColorPanel
            or WaveMenuCommand.Quit
            or WaveMenuCommand.ReopenTab
            or WaveMenuCommand.NextTab
            or WaveMenuCommand.PrevTab
            or WaveMenuCommand.CopyAllTabTimes
            or WaveMenuCommand.CopySelectionTime
            or WaveMenuCommand.CopySelectionTimePath
            or WaveMenuCommand.CopyFileName
            or WaveMenuCommand.CopyFilePath
            or WaveMenuCommand.Tips
            or WaveMenuCommand.Manual
            or WaveMenuCommand.GitHub
            => false,
        _ => true,
    };

    public static bool BlocksTransport(TransportCommand command) => command switch
    {
        TransportCommand.TogglePlayback
            or TransportCommand.Stop
            or TransportCommand.JumpToTime
            or TransportCommand.GoToStart
            or TransportCommand.PreviousPage
            or TransportCommand.PreviousMarker
            or TransportCommand.NextMarker
            or TransportCommand.NextPage
            or TransportCommand.GoToEnd
            or TransportCommand.Open
            or TransportCommand.OpenColorPanel
            or TransportCommand.ToggleTips
            or TransportCommand.OpenSettings
            or TransportCommand.OpenManual
            or TransportCommand.ToggleLibraryMaximize
            or TransportCommand.ToggleAnalyzerMaximize
            => false,
        _ => true,
    };

    /// <summary>
    /// プレイヤーで通すキー。↑↓・Home／End・PageUp／PageDown はファイル選択として別処理するので含めない。
    /// Delete はリスト除外として別処理。Ctrl+W は閉じない（リスト除外も Delete のみ）。
    /// </summary>
    public static bool AllowsKey(Key key, ModifierKeys modifiers)
    {
        if (IsModifierOnly(key))
        {
            return true;
        }

        if (key is Key.Escape or Key.F9 or Key.F10 or Key.F11 or Key.F12 or Key.Apps)
        {
            return true;
        }

        if (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3 or Key.F5)
        {
            return true;
        }

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return true;
        }

        if (key == Key.F4 && (modifiers & ModifierKeys.Alt) != 0)
        {
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.Alt)
        {
            return true;
        }

        if ((key is Key.Left or Key.Right)
            && modifiers is ModifierKeys.None or ModifierKeys.Control)
        {
            return true;
        }

        if ((key is Key.Up or Key.Down) && modifiers == ModifierKeys.Control)
        {
            return true;
        }

        if ((key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
            && (modifiers & ModifierKeys.Control) != 0
            && (modifiers & ModifierKeys.Alt) == 0)
        {
            return true;
        }

        if (IsDigitKey(key) && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return true;
        }

        return (key, modifiers) switch
        {
            (Key.Space, ModifierKeys.None or ModifierKeys.Control) => true,
            (Key.Enter, ModifierKeys.None or ModifierKeys.Alt) => true,
            (Key.L, ModifierKeys.None) => true,
            (Key.R, ModifierKeys.None) => true,
            (Key.A, ModifierKeys.Control) => true,
            (Key.F, ModifierKeys.Control) => true,
            (Key.S, ModifierKeys.Alt) => true,
            (Key.A, ModifierKeys.Alt) => true,
            (Key.O, ModifierKeys.Control) => true,
            (Key.O, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Q, ModifierKeys.Control) => true,
            (Key.C, ModifierKeys.Control) => true,
            (Key.C, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Z, ModifierKeys.Control) => true,
            (Key.T, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Tab, ModifierKeys.Control) => true,
            (Key.Tab, ModifierKeys.Control | ModifierKeys.Shift) => true,
            _ => false,
        };
    }

    /// <summary>
    /// Space 単独再生はプレイリストがアクティブなときだけ。
    /// ライブラリ（フォルダツリー）／お気に入りでは始めない。検索欄は呼び出し前に弾く。
    /// </summary>
    public static bool PlaysLibrarySelectionOnSpace(bool explorerFocused, bool favoritesFocused) =>
        !explorerFocused && !favoritesFocused;

    /// <summary>
    /// ランダム再生の切替。プレイリストが空でも、ツリー／お気に入りフォーカスでも効く。
    /// 検索欄では文字入力のまま。
    /// </summary>
    public static bool IsShuffleToggle(Key key, ModifierKeys modifiers) =>
        key == Key.R && modifiers == ModifierKeys.None;

    /// <summary>
    /// フォルダツリーの更新とフォーカス移動。プレイリスト／お気に入り／波形フォーカスでも効く。
    /// </summary>
    public static bool IsExplorerRefresh(Key key, ModifierKeys modifiers) =>
        key == Key.F5 && modifiers == ModifierKeys.None;

    /// <summary>ツリーが左右を使う。再生中の早送り／巻き戻しにはしない。</summary>
    public static bool ExplorerOwnsHorizontal(Key key, ModifierKeys modifiers) =>
        key is Key.Left or Key.Right && modifiers == ModifierKeys.None;

    /// <summary>
    /// ツリー選択は横に追従しない。行の縦位置だけビューへ入れる量。
    /// </summary>
    public static double VerticalBringIntoViewDelta(double y, double height, double viewportHeight)
    {
        if (viewportHeight <= 0 || height <= 0 || double.IsNaN(y) || double.IsNaN(height))
        {
            return 0;
        }

        if (y < 0)
        {
            return y;
        }

        var overflow = y + height - viewportHeight;
        return overflow > 0 ? overflow : 0;
    }

    /// <summary>エクスプローラーの *。テンキーと Shift+8。</summary>
    public static bool IsExplorerExpandAll(Key key, ModifierKeys modifiers) =>
        (key == Key.Multiply && modifiers == ModifierKeys.None)
        || (key == Key.D8 && modifiers == ModifierKeys.Shift);

    /// <summary>エクスプローラーの /。テンキーと Oem2（日本語キーボードの /）。</summary>
    public static bool IsExplorerCollapseSubtree(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.Divide or Key.Oem2;

    /// <summary>
    /// フォルダツリーで拒否するキー（切り取り／貼り付け／削除／リネーム等）。コピーとナビと Enter 以外はここで止める。
    /// </summary>
    public static bool BlocksExplorerKey(Key key, ModifierKeys modifiers)
    {
        if (IsModifierOnly(key))
        {
            return false;
        }

        // 設定 (Ctrl+Shift+O) など AllowsKey のアプリ共通ショートカットはツリーからでも通す。
        if (AllowsKey(key, modifiers)
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            return false;
        }

        if (modifiers is ModifierKeys.None or ModifierKeys.Shift
            && key is Key.Up or Key.Down or Key.Left or Key.Right
                or Key.Home or Key.End or Key.PageUp or Key.PageDown
                or Key.Add or Key.Subtract or Key.OemPlus or Key.OemMinus)
        {
            return false;
        }

        if (IsExplorerExpandAll(key, modifiers) || IsExplorerCollapseSubtree(key, modifiers))
        {
            return false;
        }

        if (key == Key.Enter && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return false;
        }

        // アプリ共通のモード切替・終了、プレイヤーのペイン切替はツリーからでも通す。
        if (key is Key.Escape or Key.F10 or Key.F11 or Key.F12 or Key.Apps)
        {
            return false;
        }

        if (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3 or Key.F5)
        {
            return false;
        }

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return false;
        }

        if (key == Key.F4 && (modifiers & ModifierKeys.Alt) != 0)
        {
            return false;
        }

        if (key == Key.Space && modifiers == ModifierKeys.Alt)
        {
            return false;
        }

        // Space 単独再生はプレイリストだけ。ツリーではアイコン選択などにも使わせない。
        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            return true;
        }

        if ((key == Key.Q && modifiers == ModifierKeys.Control)
            || (key == Key.S && (modifiers & ModifierKeys.Alt) != 0))
        {
            return false;
        }

        // 削除・バックスペース・切り取り／貼り付け・複製などファイル操作系は止める。Ctrl+C はコピーとして通す。
        // F2 はプレイヤーでお気に入りフォーカスに使うので、ここでは止めない。
        if (key is Key.Delete or Key.Back)
        {
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0
            && key is Key.X or Key.V or Key.D or Key.A or Key.N or Key.R)
        {
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0
            && (modifiers & ModifierKeys.Shift) != 0
            && key is Key.D or Key.N or Key.C or Key.V)
        {
            return true;
        }

        // 文字キーのタイプアヘッドは許可（フォルダ名ジャンプ）。上記以外の修飾付きは止める。
        if (modifiers != ModifierKeys.None && modifiers != ModifierKeys.Shift)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// プレイヤーを抜けるときに残すセッション。リスト選択があればそれ、なければ今のファイル。
    /// </summary>
    public static DocumentSession[] SessionsToKeep(
        IReadOnlyCollection<DocumentSession> selected,
        DocumentSession? active)
    {
        if (selected.Count > 0)
        {
            var copy = new DocumentSession[selected.Count];
            var i = 0;
            foreach (var session in selected)
            {
                copy[i++] = session;
            }

            return copy;
        }

        return active is null ? [] : [active];
    }

    public static DocumentSession[] ExcludeEditorBlocked(IReadOnlyList<DocumentSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return [];
        }

        var keep = new List<DocumentSession>(sessions.Count);
        foreach (var session in sessions)
        {
            if (!LibraryPlaylistDocuments.BlocksEditor(session.Document))
            {
                keep.Add(session);
            }
        }

        return [.. keep];
    }

    public static DocumentSession[] SessionsToDrop(
        IReadOnlyList<DocumentSession> all,
        IReadOnlyCollection<DocumentSession> keep)
    {
        if (keep.Count == 0)
        {
            return all.Count == 0 ? [] : [.. all];
        }

        var kept = new HashSet<DocumentSession>(keep);
        var drop = new List<DocumentSession>();
        foreach (var session in all)
        {
            if (!kept.Contains(session))
            {
                drop.Add(session);
            }
        }

        return [.. drop];
    }

    /// <summary>エディタ復帰でフル PCM が必要なストリーム／遅延読み込み。動画／PDF は昇格しない。</summary>
    public static bool NeedsEditorPcmUpgrade(AudioDocument document) =>
        !LibraryPlaylistDocuments.BlocksEditor(document)
        && (document.IsDeferredLoad || document.IsStreamPlayback);

    /// <summary>
    /// プレイヤーの完成ピークをそのまま出してよいか。エディタではチャンネル数と粒度が要る。
    /// </summary>
    public static bool CanReusePeaks(bool playerMode, AudioDocument document)
    {
        if (document.Peaks.IsEmpty || document.Peaks.IsBuilding)
        {
            return false;
        }

        return playerMode || !document.Peaks.NeedsEditorRebuild(document.Channels);
    }

    /// <summary>
    /// 同じ曲のピーク差し替え。完成を走査中スナップショットで戻さない。
    /// 推定尺と実デコードが数フレームずれると FrameCount だけ見て古い途中結果が勝ってしまう。
    /// </summary>
    public static bool IsNewerLibraryPeaks(PeakPyramid current, PeakPyramid incoming)
    {
        if (incoming.IsEmpty)
        {
            return false;
        }

        if (current.IsEmpty)
        {
            return true;
        }

        if (!current.IsBuilding && incoming.IsBuilding)
        {
            return false;
        }

        if (!incoming.IsBuilding && current.IsBuilding)
        {
            return true;
        }

        if (current.Channels != incoming.Channels)
        {
            return !incoming.IsBuilding || incoming.FilledFrames >= current.FilledFrames;
        }

        if (current.FrameCount != incoming.FrameCount)
        {
            return !incoming.IsBuilding || incoming.FilledFrames >= current.FilledFrames;
        }

        return incoming.FilledFrames >= current.FilledFrames;
    }

    /// <summary>
    /// 既に PCM があるファイルだけピークを作り直す。ストリームはフル Load が兼ねる。
    /// プレイヤー包絡（モノラル）のままだと、エディタでレーンが 1 本しか出ない。
    /// </summary>
    public static bool NeedsEditorPeakUpgrade(AudioDocument document) =>
        !NeedsEditorPcmUpgrade(document) && !CanReusePeaks(playerMode: false, document);

    /// <summary>プレイヤーに入ったときに選んで再生する先頭のファイル。</summary>
    public static DocumentSession? FirstSession(IReadOnlyList<DocumentSession> sessions) =>
        sessions.Count == 0 ? null : sessions[0];

    /// <summary>
    /// 既定 Wave フォーマットと違うレート／ビット／Ch。0（未確定）は着色しない。
    /// 着色は該当セルだけ（行全体ではない）。色は <see cref="FormatMismatchForeBrushKey"/>。
    /// </summary>
    public static bool HighlightsFormatValue(int value, int defaultValue) =>
        value > 0 && value != defaultValue;

    /// <summary>既定 Wave フォーマットと違うプレイリスト文字。色設定のプレイヤー項目。</summary>
    public const string FormatMismatchForeBrushKey = "PlayerFormatMismatchForeBrush";

    /// <summary>
    /// 開いている波形が無いとき、レベルメーター／スペアナ／ラウドネス／ゴニオを出さない。
    /// プレイヤーもエディタも同じ。
    /// </summary>
    public static bool ShowsPlayerMeters(int playlistCount) => playlistCount > 0;

    /// <summary>
    /// 音声信号で動くメーターはフェードせず即出す。停止中の追加だけ 1 秒フェード。
    /// </summary>
    public static bool InstantPlayerMeterReveal(bool show, bool signalMoving) =>
        show && signalMoving;

    /// <summary>
    /// プレイヤー突入は消えた状態から。空リストで一度出してフェードアウトしない。
    /// 再生中の即表示は先に消さない。
    /// </summary>
    public static bool SnapPlayerMetersHiddenOnEnter(bool enteringPlayer, bool instantReveal) =>
        enteringPlayer && !instantReveal;

    /// <summary>メーターのフェード。ジャケットのクロスフェードと同じ 1 秒。</summary>
    public const double MeterFadeSeconds = 1;

    /// <summary>ホバー↔本再生／PDF 表示の前面クローム。メーター用の 1 秒より短く。</summary>
    public const double ChromeFadeSeconds = 0.2;

    public const int MeterFadeFrameRate = 30;

    /// <summary>
    /// 動画再生中、または PDF 表示モード中は前面クロームを隠す。
    /// 選択プレビュー（暗表示）ではクロームを残す。
    /// 上下キーなどで次の本再生へつなぐあいだは <paramref name="bridgeHold"/> で隠したままにする。
    /// </summary>
    public static bool HidesChromeForPlaylistVisual(
        bool player,
        bool visualShown,
        bool visualPlaying,
        bool isPdf,
        bool isVideo,
        bool bridgeHold = false)
    {
        if (!player || !visualShown)
        {
            return false;
        }

        if (bridgeHold && (isPdf || isVideo || visualPlaying))
        {
            return true;
        }

        if (!visualPlaying)
        {
            return false;
        }

        return isPdf || isVideo;
    }

    /// <summary>
    /// 映像／PDF の本再生から次の映像／PDF 本再生へ移るとき、クロームを出さない。
    /// </summary>
    public static bool HoldsPlaylistVisualChromeBridge(
        bool currentVisualPlaying,
        bool nextIsVisual) =>
        currentVisualPlaying && nextIsVisual;

    /// <summary>
    /// PDF の明るい表示に入った瞬間だけ HUD を閉じる。
    /// 暗い選択プレビューでセッションを立てると、Space／Enter 後にアナライザが残る。
    /// </summary>
    public static bool StartsPdfChromeHudSession(bool isPdf, bool chromeHidden, bool sessionActive) =>
        isPdf && chromeHidden && !sessionActive;

    /// <summary>明るい PDF 表示中だけセッションを維持（A トグルを覚える）。</summary>
    public static bool HoldsPdfChromeHudSession(bool isPdf, bool chromeHidden) =>
        isPdf && chromeHidden;

    /// <summary>
    /// PDF の HUD 閉じセッションを終えて動画本再生へ移ったとき、アナライザ HUD を戻す。
    /// クロームをつないだまま移ると !hide の復帰経路に乗らない。
    /// </summary>
    public static bool RestoresVideoHudAfterPdfSession(
        bool pdfSessionWasActive,
        bool stillHoldsPdfSession,
        bool chromeHidden,
        bool isVideo) =>
        pdfSessionWasActive && !stillHoldsPdfSession && chromeHidden && isVideo;

    /// <summary>再生中の映像へ合わせ直す最小ずれ。これ未満のシークは MediaElement を落とす。</summary>
    public const double VideoClockSeekSeconds = 1;

    /// <summary>1／3 早送り・巻き戻し中は、ほぼ毎フレーム映像を音声へ合わせる。</summary>
    public const double VideoShuttleSeekSeconds = 1.0 / 60;

    /// <summary>
    /// 動画ファイルは映像クロックでシークバーを進める（本再生・1/4 速プレビュー）。
    /// wave / mp3 などは音声側（AudioPlayer）の再生ヘッドを使う。
    /// </summary>
    public static bool DrivesPlayheadWhileVideoPlays(
        bool playerMode,
        bool isVideoFile,
        bool videoClockRunning) =>
        playerMode && isVideoFile && videoClockRunning;

    /// <summary>
    /// プレイヤーで曲を切り替えても、ホバー映像のシークバー用タイマーを殺さない。
    /// Bind は ResetWorkspaceInteraction を呼ぶが、1本目は既にバインド済みでスキップされ、2本目だけ止まる。
    /// </summary>
    public static bool PreservesVisualPlayheadOnBind(bool playerMode) => playerMode;

    /// <summary>
    /// 同じ動画のホバー／暗いプレビュー中に本再生へ移るときは、いまの映像位置から続ける。
    /// 別ファイルへ移るとき（上下キーなど）は先頭から。
    /// </summary>
    public static long HoverResumeFrame(
        bool hoverVideoShown,
        bool visualPlaying,
        bool sameClip,
        double positionSeconds,
        int sampleRate,
        long frameCount)
    {
        if (!hoverVideoShown || visualPlaying || !sameClip)
        {
            return 0;
        }

        var rate = Math.Max(1, sampleRate);
        var frame = (long)Math.Round(Math.Max(0, positionSeconds) * rate);
        if (frameCount <= 0)
        {
            return frame;
        }

        return Math.Clamp(frame, 0, frameCount);
    }

    public static long FrameFromSeconds(double seconds, int sampleRate, long frameCount)
    {
        var rate = Math.Max(1, sampleRate);
        var frame = (long)Math.Round(Math.Max(0, seconds) * rate);
        return Math.Clamp(frame, 0, Math.Max(0, frameCount));
    }

    /// <summary>MediaElement の尺がまだ無いときは、タグ／mvhd の尺でシークバーを進める。</summary>
    public static TimeSpan VisualDurationOrFallback(TimeSpan natural, TimeSpan fallback)
    {
        if (natural > TimeSpan.Zero)
        {
            return natural;
        }

        return fallback > TimeSpan.Zero ? fallback : TimeSpan.Zero;
    }

    /// <summary>
    /// MediaElement.Position は無音だと更新されないことがあるので、経過時間で再生位置を出す。
    /// </summary>
    public static TimeSpan VisualPlayPosition(
        TimeSpan origin,
        double elapsedSeconds,
        double speed,
        TimeSpan duration)
    {
        var seconds = origin.TotalSeconds + (Math.Max(0, elapsedSeconds) * speed);
        if (seconds < 0)
        {
            return TimeSpan.Zero;
        }

        var pos = TimeSpan.FromSeconds(seconds);
        if (duration > TimeSpan.Zero && pos > duration)
        {
            return duration;
        }

        return pos;
    }

    /// <summary>選択範囲ループ。範囲が無ければそのまま。</summary>
    public static long WrapLoopFrame(long frame, long start, long end)
    {
        var length = end - start;
        if (length <= 0)
        {
            return frame;
        }

        if (frame < start)
        {
            return start;
        }

        if (frame < end)
        {
            return frame;
        }

        return start + ((frame - start) % length);
    }

    public static bool NeedsLoopSeek(long frame, long start, long end) =>
        end > start && (frame < start || frame >= end);

    /// <summary>ブラウザの F11 と同じ、モニタ一面の全画面（動画／PDF）。解除もユーザー操作のみ。</summary>
    public static bool IsVideoFullscreenToggle(Key key, ModifierKeys modifiers) =>
        key == Key.F && modifiers == ModifierKeys.None;

    /// <summary>波形・アナライザ・ファイル名の表示切替（タイムコードは含まない）。</summary>
    public static bool IsVideoHudToggle(Key key, ModifierKeys modifiers) =>
        key == Key.A && modifiers == ModifierKeys.None;

    /// <summary>動画本再生中の大きなタイムコード（選択範囲）の表示切替。</summary>
    public static bool IsVideoTimecodeToggle(Key key, ModifierKeys modifiers) =>
        key == Key.T && modifiers == ModifierKeys.None;

    /// <summary>前面クロームが消えている本再生中だけ、タイムコードを出せる。</summary>
    public static bool ShowsPlaylistVideoTimecode(bool chromeHidden, bool timecodeOn, bool isVideo) =>
        chromeHidden && timecodeOn && isVideo;

    /// <summary>前面クロームが消えている動画本再生中、A で HUD が出ているときだけ左下のファイル名を出す。</summary>
    public static bool ShowsPlaylistVideoFileName(bool chromeHidden, bool hudOn, bool isVideo) =>
        chromeHidden && hudOn && isVideo;

    /// <summary>PDF ページを静止背景レイヤーへ固定／解除（固定時は表示を閉じる）。</summary>
    public static bool IsPdfBackgroundPinToggle(Key key, ModifierKeys modifiers) =>
        key == Key.B && modifiers == ModifierKeys.None;

    /// <summary>
    /// 明るい PDF 表示を Enter／Space で抜けるときは暗いプレビューへ戻す。
    /// B で静止背景に固定したあとはプレビューを出さず、固定レイヤーだけ残す。
    /// </summary>
    public static bool RestoresPdfDimPreviewOnExit(bool backgroundPinned) => !backgroundPinned;

    /// <summary>
    /// プレイリストの Enter。再生中は止め、停止中だけ先頭から連続再生を始める。
    /// </summary>
    public static bool StartsLibraryPlaybackOnEnter(bool playbackActive) => !playbackActive;

    /// <summary>PDF 表示中のページめくり・先頭／末尾。</summary>
    public static bool IsPdfPageKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None
        && key is Key.Left or Key.Right or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    /// <summary>キーボード／テンキーの +。</summary>
    public static bool IsPdfZoomInKey(Key key, ModifierKeys modifiers) =>
        key == Key.Add && modifiers == ModifierKeys.None
        || key == Key.OemPlus && modifiers is ModifierKeys.None or ModifierKeys.Shift;

    /// <summary>キーボード／テンキーの -。</summary>
    public static bool IsPdfZoomOutKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.Subtract or Key.OemMinus;

    /// <summary>
    /// PDF 表示中はプレイリスト操作キーを奪う（除外・全選択・ページキーなど）。
    /// ↑↓ は前後ファイルへ移すので奪わない。表示を抜けると通常どおり。
    /// </summary>
    public static bool BlocksPlaylistWhilePdf(Key key, ModifierKeys modifiers)
    {
        // Home／End／Page はページ操作。↑↓ はプレイリスト移動として通す。
        if (modifiers is ModifierKeys.None or ModifierKeys.Shift
            && key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            return true;
        }

        if (key == Key.Delete && modifiers == ModifierKeys.None)
        {
            return true;
        }

        // Enter／Space は表示解除として別処理する。
        if (IsShuffleToggle(key, modifiers))
        {
            return true;
        }

        if (key == Key.A && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            return true;
        }

        if (key == Key.C && modifiers == ModifierKeys.Control)
        {
            return true;
        }

        if (key == Key.Z && modifiers == ModifierKeys.Control)
        {
            return true;
        }

        // テンキーの前後曲・再生し直しはプレイリスト操作扱い。
        if (modifiers == ModifierKeys.None && key is Key.NumPad4 or Key.NumPad5 or Key.NumPad6)
        {
            return true;
        }

        return IsPaneCycleKey(key, modifiers)
            || (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3)
            || IsPaneFocusArrow(key, modifiers);
    }

    /// <summary>プレイヤーで動画を出しているとき（本再生・ホバー）は Silent Skip をかけない。チェックボックスは触らない。</summary>
    public static bool IgnoresSilentSkipForVideo(bool player, bool videoPlaying) =>
        player && videoPlaying;

    public static DoubleAnimation CreateMeterFade(double from, double to) =>
        CreateOpacityFade(from, to, MeterFadeSeconds);

    public static DoubleAnimation CreateOpacityFade(double from, double to, double seconds)
    {
        var rising = to > from;
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(Math.Max(0, seconds)),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase
            {
                EasingMode = rising ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        };
        Timeline.SetDesiredFrameRate(anim, MeterFadeFrameRate);
        return anim;
    }

    public static void FadeElementOpacity(
        UIElement target,
        double to,
        bool instant,
        bool? hitTestVisible = null,
        double? seconds = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        // 表示中の値を残してからアニメを外す。先に外すと HoldEnd の 0 がローカル 1 に飛び、フェードインが省略される。
        var from = target.Opacity;
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = from;
        if (hitTestVisible is { } hit)
        {
            target.IsHitTestVisible = hit;
        }

        if (instant || Math.Abs(from - to) < 0.001)
        {
            target.Opacity = to;
            return;
        }

        var anim = CreateOpacityFade(from, to, seconds ?? MeterFadeSeconds);
        anim.Completed += (_, _) =>
        {
            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = to;
        };
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>映像の上に乗っているときだけ、読みやすいドロップシャドウ。</summary>
    public static void SetVideoUiShadow(UIElement target, bool on)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!on)
        {
            target.Effect = null;
            return;
        }

        if (target.Effect is DropShadowEffect)
        {
            return;
        }

        target.Effect = new DropShadowEffect
        {
            BlurRadius = 22,
            ShadowDepth = 0,
            Direction = 0,
            Color = Colors.Black,
            Opacity = 0.95,
            RenderingBias = RenderingBias.Performance,
        };
    }

    public static int EdgeIndex(int count, int edge) =>
        count <= 0 ? -1 : edge < 0 ? 0 : count - 1;

    /// <summary>
    /// プレイヤーのピーク走査を残す曲。表示中と次曲先読みだけ。
    /// 世代を進めてまとめて止めると、今の波形が途中のまま切れる。
    /// </summary>
    public static bool KeepPeakJob(AudioDocument document, AudioDocument? playing, AudioDocument? next)
    {
        if (playing is not null && ReferenceEquals(document, playing))
        {
            return true;
        }

        return next is not null && ReferenceEquals(document, next);
    }

    /// <summary>曲が終わった次。末尾の次は先頭。今の曲が見つからなければ先頭。空なら -1。</summary>
    public static int NextLoopIndex(int count, int current)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0 || current >= count - 1)
        {
            return 0;
        }

        return current + 1;
    }

    /// <summary>曲が戻る前。先頭の前は末尾。今の曲が見つからなければ末尾。空なら -1。</summary>
    public static int PreviousLoopIndex(int count, int current)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current <= 0 || current >= count)
        {
            return count - 1;
        }

        return current - 1;
    }

    /// <summary>プレイヤーのスキップ秒。テンキー 7／9。</summary>
    public const double SeekNudgeSeconds = 5;

    /// <summary>スキップ時のクロスフェード。推移元のフェードアウトと同時に、推移先をフェードイン（0.75 秒）。</summary>
    public const int SeekNudgeFadeMilliseconds = 750;

    /// <summary>7／9 押しっぱなしの最初のリピートまでの待ち。短い押しの誤リピートを防ぐ。</summary>
    public const int SeekNudgeRepeatDelayMs = 250;

    /// <summary>待ちのあとの 7／9 リピート間隔。</summary>
    public const int SeekNudgeRepeatIntervalMs = 200;

    public static int SeekNudgeTimerIntervalMs(bool repeatStarted) =>
        repeatStarted ? SeekNudgeRepeatIntervalMs : SeekNudgeRepeatDelayMs;

    /// <summary>
    /// 押しっぱなしのリピートは Background タイマーだと、描画が重いと Tick が落ちる。
    /// Send なら CompositionTarget.Rendering より先に進む。
    /// </summary>
    public const DispatcherPriority SeekNudgeTimerPriority = DispatcherPriority.Send;

    /// <summary>遅延した Tick で一度に足す上限。止めすぎず、溜め込みすぎない。</summary>
    public const int SeekNudgeCatchUpMax = 3;

    public static int SeekNudgeCatchUpSteps(long elapsedMs, bool repeatStarted)
    {
        var interval = SeekNudgeTimerIntervalMs(repeatStarted);
        if (elapsedMs < interval || interval <= 0)
        {
            return 1;
        }

        return (int)Math.Clamp(elapsedMs / interval, 1, SeekNudgeCatchUpMax);
    }

    public static double SeekNudgeCatchUpSeconds(long elapsedMs, bool repeatStarted) =>
        SeekNudgeSeconds * SeekNudgeCatchUpSteps(elapsedMs, repeatStarted);

    /// <summary>
    /// 再生ヘッド同期の最短間隔。144Hz でも 60fps 相当に抑え、7／9 のタイマーを飢餓させない。
    /// </summary>
    public const int PlaybackVisualMinIntervalMs = 16;

    /// <summary>
    /// スペアナ／ゴニオ／ラウドネスは CompositionTarget.Rendering（Render）に載せない。
    /// Render は Input より先なので、重いとマウスとキーがワンテンポ遅れる。
    /// </summary>
    public const DispatcherPriority AnalyzerTickPriority = DispatcherPriority.Background;

    /// <summary>再生ヘッドが同じピクセルに居るときの再描画間隔。残光の減衰用。</summary>
    public const int PlayheadIdleInvalidateMs = 50;

    public const double PlayheadMoveEpsilonPx = 0.5;

    public static bool ShouldRefreshPlayheadPaint(double lastX, long lastAtMs, double nextX, long nowMs)
    {
        if (double.IsNaN(lastX) || Math.Abs(nextX - lastX) >= PlayheadMoveEpsilonPx)
        {
            return true;
        }

        return nowMs - lastAtMs >= PlayheadIdleInvalidateMs;
    }

    public static bool PlayerNumpadRepeatIsHold(LibraryNumpadCommand command) =>
        command is LibraryNumpadCommand.Rewind
            or LibraryNumpadCommand.FastForward
            or LibraryNumpadCommand.SeekBack
            or LibraryNumpadCommand.SeekForward;

    public static bool IsPlayerNumpadKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is >= Key.NumPad0 and <= Key.NumPad9;

    public static bool IsPlayerShuttleKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.NumPad1 or Key.NumPad3;

    public static bool IsPlayerSeekNudgeKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.NumPad7 or Key.NumPad9;

    public static LibraryNumpadCommand PlayerNumpadCommand(Key key, ModifierKeys modifiers)
    {
        if (!IsPlayerNumpadKey(key, modifiers))
        {
            return LibraryNumpadCommand.None;
        }

        return key switch
        {
            Key.NumPad0 => LibraryNumpadCommand.PlayPause,
            Key.NumPad1 => LibraryNumpadCommand.Rewind,
            Key.NumPad3 => LibraryNumpadCommand.FastForward,
            Key.NumPad4 => LibraryNumpadCommand.PreviousTrack,
            Key.NumPad5 => LibraryNumpadCommand.Restart,
            Key.NumPad6 => LibraryNumpadCommand.NextTrack,
            Key.NumPad7 => LibraryNumpadCommand.SeekBack,
            Key.NumPad9 => LibraryNumpadCommand.SeekForward,
            _ => LibraryNumpadCommand.None,
        };
    }

    public static int PageStep(int visibleRows) =>
        Math.Max(1, visibleRows - 1);

    public static bool IsPaneCycleKey(Key key, ModifierKeys modifiers) =>
        key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift;

    /// <summary>
    /// Ctrl+←／↑ ライブラリ、Ctrl+→ プレイリスト、Ctrl+↓ お気に入り。
    /// エディタのマーカー移動／ズームは引き継がない。
    /// </summary>
    public static bool IsPaneFocusArrow(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control
        && key is Key.Left or Key.Right or Key.Up or Key.Down;

    /// <summary>
    /// Ctrl+F の行き先。プレイリストならリスト検索、ツリー／お気に入りはライブラリ検索。
    /// </summary>
    public static LibraryPane SearchPane(LibraryPane current) =>
        current == LibraryPane.List ? LibraryPane.List : LibraryPane.Explorer;

    /// <summary>Tab：ツリー → お気に入り → プレイリスト → ツリー。Shift+Tab は逆。</summary>
    public static LibraryPane NextPane(LibraryPane current, bool reverse) =>
        reverse
            ? current switch
            {
                LibraryPane.List => LibraryPane.Favorites,
                LibraryPane.Favorites => LibraryPane.Explorer,
                _ => LibraryPane.List,
            }
            : current switch
            {
                LibraryPane.Explorer => LibraryPane.Favorites,
                LibraryPane.Favorites => LibraryPane.List,
                _ => LibraryPane.Explorer,
            };

    private static bool IsModifierOnly(Key key) =>
        key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.CapsLock or Key.NumLock or Key.Scroll;

    /// <summary>
    /// IME が上段／テンキーの数字を ImeProcessed にした場合、割合ジャンプとテンキー操作へ戻す。
    /// かな入力の文字キーはそのまま（A などのショートカットにしない）。
    /// </summary>
    public static Key ResolveDigitKey(Key key, Key imeProcessedKey) =>
        key == Key.ImeProcessed && IsDigitKey(imeProcessedKey) ? imeProcessedKey : key;

    private static bool IsDigitKey(Key key) =>
        key is >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9;
}

internal enum LibraryPane
{
    Explorer,
    Favorites,
    List,
}

internal enum LibraryNumpadCommand
{
    None,
    PlayPause,
    Restart,
    PreviousTrack,
    NextTrack,
    SeekBack,
    SeekForward,
    Rewind,
    FastForward,
}
