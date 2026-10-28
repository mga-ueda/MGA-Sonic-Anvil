using System.Windows;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Video.Dxgi;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイリスト動画は D3D11＋DXGI で描く（MediaElement 経路は無い）。
/// 再生位置は既存の映像クロック（CurrentVisualPosition）にタイマーで追従するだけ。
/// D3DImage 経由の普通の WPF 要素なので、上の暗幕・格子・HUD・クローム／背面の光漏れはそのまま重なる（airspace なし）。
/// フェードは <see cref="_dxgiHost"/> の Opacity（光漏れホストも追従）。デコードは <see cref="DxgiAsyncFramePump"/> の
/// ワーカーが行い、タイマーは位置を要求してキャッシュを引くだけ。
/// </summary>
internal sealed partial class LibraryVisualStage
{
    private const double DxgiFrameTickMs = 33;
    private const int DxgiTestPatternWidth = 1280;
    private const int DxgiTestPatternHeight = 720;
    private static readonly TimeSpan DxgiTestPatternDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DxgiEndedRearm = TimeSpan.FromMilliseconds(250);

    private readonly DxgiVideoHost _dxgiHost = new() { Visibility = Visibility.Collapsed };
    private readonly DxgiAsyncFramePump _dxgiPump = new();
    private DispatcherTimer? _dxgiTimer;
    private byte[]? _dxgiPattern;

    /// <summary>直近に描いたフレームと、そのときのホストの RenderVersion（同じなら描き直さない）。</summary>
    private DxgiDecodedFrame? _dxgiLastFrame;
    private int _dxgiLastRenderVersion = -1;

    private TimeSpan _dxgiDuration;
    private int _dxgiWidth;
    private int _dxgiHeight;
    private int _dxgiOpenTicket;
    private bool _dxgiEnded;

    private bool UsingDxgi => _video;

    private void SyncVideoPresenterVisibility()
    {
        if (!UsingDxgi)
        {
            _dxgiHost.Visibility = Visibility.Collapsed;
            ClearAmbientSpill();
            return;
        }

        _dxgiHost.Visibility = Visibility.Visible;
        StartDxgiTimer();
        SyncAmbientSpill();
    }

    /// <summary>
    /// 同じパスでも次の ShowVideo で開き直す（プロキシ作り直しで読み手を一度閉じたとき）。
    /// </summary>
    private bool _dxgiReopenPending;

    /// <summary>
    /// プロキシの作り直し前に、DXGI が掴んでいる <paramref name="path"/> を閉じる（上書き Move が失敗しないように）。
    /// 閉じたら true。次の ShowVideo は同じパスでも開き直す。
    /// </summary>
    internal bool ReleaseDxgiSourceForRewrite(string path)
    {
        if (!UsingDxgi
            || string.IsNullOrWhiteSpace(path)
            || !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        StopDxgiPresenter();
        _dxgiReopenPending = true;
        return true;
    }

    private bool ConsumeDxgiReopenPending()
    {
        var pending = _dxgiReopenPending && UsingDxgi;
        _dxgiReopenPending = false;
        return pending;
    }

    /// <summary>
    /// 呼び出し側は TryGetDisplayPath 済みのはずだが、原ファイルが渡ってもプロキシがあればそちらを開く。
    /// キャッシュ内のパスは引き直さない（二重プロキシ防止）。
    /// </summary>
    private static string ResolveDxgiOpenPath(string path)
    {
        if (VideoProxy.IsProxyPath(path)
            || !LibraryPlaylistDocuments.IsVideo(path)
            || !VideoProxy.TryGetCached(path, out var proxy))
        {
            return path;
        }

        return proxy;
    }

    private void OpenDxgiSource(string path)
    {
        var ticket = ++_dxgiOpenTicket;
        _dxgiEnded = false;
        _dxgiLastFrame = null;
        _dxgiReopenPending = false;
        var openPath = ResolveDxgiOpenPath(path);
        // MJPEG プロキシは TV レンジで焼いてある（VideoProxy.ProxyVideoFilter）。MF の MJPEG デコーダは JPEG 慣例で
        // フルレンジ／不明を返しうるので、プロキシを開くときだけ色情報の Range を Limited に固定する
        // （シェーダの行列・レンジ計算はそのまま。原ファイルのフルレンジはそのまま通す）。
        if (_dxgiPump.Open(openPath, forceLimitedRange: VideoProxy.IsProxyPath(openPath)))
        {
            _dxgiDuration = _dxgiPump.Duration;
            _dxgiWidth = _dxgiPump.Width;
            _dxgiHeight = _dxgiPump.Height;
            _dxgiHost.SetSourceColorInfo(_dxgiPump.ColorInfo);
        }
        else
        {
            // MF で開けない: テストパターンで presenter だけ動かす。
            _dxgiDuration = _fallbackDuration > TimeSpan.Zero ? _fallbackDuration : DxgiTestPatternDuration;
            _dxgiWidth = DxgiTestPatternWidth;
            _dxgiHeight = DxgiTestPatternHeight;
        }

        if (_dxgiDuration > TimeSpan.Zero)
        {
            _fallbackDuration = _dxgiDuration;
        }

        SyncVideoPresenterVisibility();

        // MediaOpened と同じく非同期で通知する（ShowVideo の途中で呼び出し側へ戻さない）。
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _dxgiOpenTicket || !UsingDxgi || !IsShown)
                {
                    return;
                }

                SyncAmbientSpill();
                Opened?.Invoke(this, Duration);
                if (!_playing && !_framePaused)
                {
                    RestartHoverPreviewAtStart();
                }
            },
            DispatcherPriority.Background);
    }

    private void StopDxgiPresenter()
    {
        _dxgiOpenTicket++;
        _dxgiTimer?.Stop();
        _dxgiPump.Close();
        _dxgiLastFrame = null;
        _dxgiDuration = TimeSpan.Zero;
        _dxgiWidth = 0;
        _dxgiHeight = 0;
        _dxgiEnded = false;
        ClearAmbientSpill();
        if (_dxgiHost.Visibility == Visibility.Visible)
        {
            // 次に出したとき前のクリップの最終フレームが一瞬見えないように。
            _dxgiHost.Clear();
        }

        _dxgiHost.Visibility = Visibility.Collapsed;
    }

    private void StartDxgiTimer()
    {
        if (_dxgiTimer is null)
        {
            _dxgiTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(DxgiFrameTickMs),
            };
            _dxgiTimer.Tick += (_, _) => PresentDxgiFrame();
        }

        if (!_dxgiTimer.IsEnabled)
        {
            _dxgiTimer.Start();
        }
    }

    private void PresentDxgiFrame()
    {
        if (!UsingDxgi || !IsShown)
        {
            _dxgiTimer?.Stop();
            return;
        }

        var presenter = _dxgiHost.Presenter;
        if (presenter is null || !presenter.IsReady)
        {
            return;
        }

        var pos = CurrentVisualPosition();
        if (_dxgiPump.IsOpen)
        {
            // シャトル（テンキー 1／3: 音声 3 倍、映像クロック停止で Seek が音声ヘッドに追従）中は、
            // ポンプの先読みを広げ Seek を控えて、遅れても止まらず追いつかせる。
            _dxgiPump.SetShuttleMode(LibraryPlayerMode.VideoSeekIsShuttleScrub(_playing, _visualClock.IsRunning));

            // 直前に出した 1 枚がまだこの位置を覆っている（一時停止・スクラブ静止・同じフレーム内の tick）なら、
            // ワーカーを起こさず（デコード圧力なし）、Present もしない（RT に残っている）。
            // サーフェスが作り直されていた（RenderVersion が進んだ）ときは保持せず、いつもの経路でキャッシュから描き直す
            //（持ち越した参照の画素はプール再利用で書き換わり得るので、古い参照は描かない）。
            var last = _dxgiLastFrame;
            var hold = last is not null
                && _dxgiHost.RenderVersion == _dxgiLastRenderVersion
                && DxgiFrameHold.ShouldHold(last.PtsTicks, last.DurationTicks, pos.Ticks, _dxgiPump.FrameDuration.Ticks);
            if (!hold)
            {
                // ワーカーに位置を伝え、手元にある分だけ描く。無ければ前のフレームを保持（RT に残っている）。
                _dxgiPump.RequestPosition(pos);
                if (_dxgiPump.TryGetFrame(pos, out var frame) && frame is not null)
                {
                    var version = _dxgiHost.RenderVersion;
                    if (!ReferenceEquals(frame, _dxgiLastFrame) || version != _dxgiLastRenderVersion)
                    {
                        _dxgiHost.PresentFrame(frame);
                        _dxgiLastFrame = frame;
                        _dxgiLastRenderVersion = _dxgiHost.RenderVersion;
                        // 光漏れは本編と同じフレームから作る（二重デコードなし）。新フレームのときだけ縮小して書く。
                        if (_spillHost.Visibility == Visibility.Visible)
                        {
                            UpdateAmbientSpillFrame(frame);
                        }
                    }
                }
            }
        }
        else if (_dxgiWidth > 0 && _dxgiHeight > 0)
        {
            var size = _dxgiWidth * _dxgiHeight * 4;
            if (_dxgiPattern is null || _dxgiPattern.Length != size)
            {
                _dxgiPattern = new byte[size];
            }

            DxgiPrototypeFrameSource.FillTestPattern(_dxgiPattern, pos, _dxgiWidth, _dxgiHeight);
            _dxgiHost.PresentBgraFrame(_dxgiPattern, _dxgiWidth, _dxgiHeight, _dxgiWidth * 4);
        }

        // 黒帯が出る映像で光漏れがまだ出ていなければ、寸法確定後のここで出す（以後は present ごとに更新）。
        if (_spillHost.Visibility != Visibility.Visible)
        {
            SyncAmbientSpill();
        }

        RaiseDxgiEndedIfReached(pos);
    }

    /// <summary>
    /// スクラブ プレビュー（ドラッグ中の粗い絵: Seek＋数枚だけ）を DXGI ポンプに伝える。
    /// false は静止後の正確な絵／通常再生へ戻るとき。
    /// </summary>
    private void SetDxgiScrubPreview(bool preview) =>
        _dxgiPump.SetScrubPreviewMode(preview);

    /// <summary>本再生でクロックが尺に達したら 1 回だけ Ended。</summary>
    private void RaiseDxgiEndedIfReached(TimeSpan pos)
    {
        var duration = Duration;
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        if (_playing && !_framePaused && pos >= duration)
        {
            if (!_dxgiEnded)
            {
                _dxgiEnded = true;
                Ended?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (pos < duration - DxgiEndedRearm)
        {
            _dxgiEnded = false;
        }
    }
}
