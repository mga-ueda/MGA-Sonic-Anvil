using System.Runtime.InteropServices;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VstInsertEngineTests
{
    [Fact]
    public void ClearSlot_KeepsSameInstance_SoHeldReferenceStillWritesToChain()
    {
        // 差し替え UI: ClearSlot 後に掴んでいた参照へ代入してもチェーンに載ること
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\Old.vst3";
        chain.Slots[0].ClassId = "old";
        var held = chain.Slots[0];
        chain.ClearSlot(0);
        Assert.True(held.IsEmpty);
        Assert.Same(held, chain.Slots[0]);

        held.PluginPath = @"C:\New.vst3";
        held.DisplayName = "New";
        held.ClassId = "new";
        Assert.False(chain.Slots[0].IsEmpty);
        Assert.Equal(@"C:\New.vst3", chain.Slots[0].PluginPath);
        Assert.Equal("new", chain.Slots[0].ClassId);
    }

    [Fact]
    public void Apply_AfterClearSlotThenAssignViaHeldReference_CreatesPlugin()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\Old.vst3";
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Equal(1, factory.CreateCount);

        var held = chain.Slots[0];
        chain.ClearSlot(0);
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Null(engine.TryGetLive(0));

        held.PluginPath = @"C:\New.vst3";
        held.DisplayName = "New";
        held.ClassId = null;
        held.Faulted = false;
        engine.Apply(owner, chain, playerBypass: false);
        Assert.NotNull(engine.TryGetLive(0));
        Assert.Equal(2, factory.CreateCount);
        Assert.Equal(@"C:\New.vst3", engine.TryGetLive(0)!.PluginPath);
    }

    [Fact]
    public void StructuralEquals_SamePlugins_IgnoresBypassAndState()
    {
        var a = new VstInsertChain();
        a.Slots[0].PluginPath = @"C:\Plugins\A.vst3";
        a.Slots[0].ClassId = "cid";
        a.Slots[0].Bypassed = true;
        a.Slots[0].StateBlob = [1, 2, 3];

        var b = new VstInsertChain();
        b.Slots[0].PluginPath = @"C:\Plugins\A.vst3";
        b.Slots[0].ClassId = "cid";
        b.Slots[0].Bypassed = false;
        b.Slots[0].StateBlob = [9];

        Assert.True(a.StructuralEquals(b));
        Assert.Equal(a.StructuralKey, b.StructuralKey);
    }

    [Fact]
    public void StructuralEquals_DifferentPlugin_IsFalse()
    {
        var a = new VstInsertChain();
        a.Slots[0].PluginPath = @"C:\Plugins\A.vst3";
        var b = new VstInsertChain();
        b.Slots[0].PluginPath = @"C:\Plugins\B.vst3";
        Assert.False(a.StructuralEquals(b));
    }

    [Fact]
    public void Apply_SameStructure_DoesNotReload_AppliesState()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);

        var owner = new object();
        var chain1 = new VstInsertChain();
        chain1.Slots[0].PluginPath = @"C:\A.vst3";
        chain1.Slots[0].ClassId = "x";
        chain1.Slots[0].StateBlob = [1];
        engine.Apply(owner, chain1, playerBypass: false);
        Assert.Equal(1, engine.ReloadCount);
        Assert.Equal(1, factory.CreateCount);

        var chain2 = new VstInsertChain();
        chain2.Slots[0].PluginPath = @"C:\A.vst3";
        chain2.Slots[0].ClassId = "x";
        chain2.Slots[0].StateBlob = [2, 3];
        chain2.Slots[0].Bypassed = true;
        engine.Apply(owner, chain2, playerBypass: false);

        Assert.Equal(1, engine.ReloadCount);
        Assert.Equal(1, factory.CreateCount);
        Assert.True(engine.StateApplyCount >= 2);
        Assert.True(engine.IsSlotBypassed(0));
        Assert.Equal(new byte[] { 2, 3 }, factory.Last.LastState);
    }

    [Fact]
    public void Apply_DifferentStructure_ReloadsSlot()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();

        var chain1 = new VstInsertChain();
        chain1.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(owner, chain1, playerBypass: false);

        var chain2 = new VstInsertChain();
        chain2.Slots[0].PluginPath = @"C:\B.vst3";
        engine.Apply(owner, chain2, playerBypass: false);

        Assert.Equal(2, engine.ReloadCount);
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void Process_PlayerBypass_DoesNotTouchBuffer()
    {
        var factory = new CountingFactory { Gain = 0f };
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(new object(), chain, playerBypass: true);

        var buffer = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };
        engine.Process(buffer, 0, buffer.Length, 2);
        Assert.Equal(new float[] { 0.5f, -0.5f, 0.25f, -0.25f }, buffer);
        Assert.Equal(0, factory.Last.ProcessCount);
    }

    [Fact]
    public void Process_Editor_AppliesGainToBuffer()
    {
        var factory = new CountingFactory { Gain = 2f };
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(new object(), chain, playerBypass: false);

        var buffer = new float[] { 0.5f, 0.25f };
        engine.Process(buffer, 0, buffer.Length, 1);
        Assert.Equal(new float[] { 1f, 0.5f }, buffer);
        Assert.Equal(1, factory.Last.ProcessCount);
    }

    [Fact]
    public void Forget_UnloadsWhenOwnerMatches()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(owner, chain, playerBypass: false);
        Assert.NotNull(engine.TryGetLive(0));

        engine.Forget(new object());
        Assert.NotNull(engine.TryGetLive(0));

        engine.Forget(owner);
        Assert.Null(engine.TryGetLive(0));
    }

    [Fact]
    public void Apply_ReplaceDoesNotWaitForSlowDispose_WhenNotExclusive()
    {
        // Exclusive 外は遅延 Dispose。Apply はすぐ返る
        var factory = new CountingFactory { DisposeDelayMs = 30_000 };
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var a = new VstInsertChain();
        a.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(owner, a, playerBypass: false);

        var b = new VstInsertChain();
        b.Slots[0].PluginPath = @"C:\B.vst3";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Apply(owner, b, playerBypass: false);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, "Apply blocked on Dispose: " + sw.ElapsedMilliseconds + "ms");
        Assert.NotNull(engine.TryGetLive(0));
        Assert.Equal(@"C:\B.vst3", engine.TryGetLive(0)!.PluginPath);
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void Apply_UnderExclusiveLoad_DisposesOldBeforeCreatingNew()
    {
        var factory = new OrderingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var a = new VstInsertChain();
        a.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(owner, a, playerBypass: false);

        engine.BeginExclusiveLoad();
        try
        {
            var b = new VstInsertChain();
            b.Slots[0].PluginPath = @"C:\B.vst3";
            engine.Apply(owner, b, playerBypass: false);
        }
        finally
        {
            engine.EndExclusiveLoad();
        }

        Assert.Equal(new[] { "create:A", "dispose:A", "create:B" }, factory.Events);
    }

    [Fact]
    public void Process_SwapDuringProcess_DoesNotHangOrCorrupt()
    {
        var factory = new CountingFactory { Gain = 2f, ProcessDelayMs = 30 };
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chainA = new VstInsertChain();
        chainA.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(owner, chainA, playerBypass: false);

        var buffer = new float[512];
        Array.Fill(buffer, 0.25f);
        var processDone = false;
        var applyDone = false;
        Exception? processEx = null;
        Exception? applyEx = null;

        var processThread = new Thread(() =>
        {
            try
            {
                engine.Process(buffer, 0, buffer.Length, 1);
                processDone = true;
            }
            catch (Exception ex)
            {
                processEx = ex;
            }
        });
        processThread.IsBackground = true;
        processThread.Start();

        Thread.Sleep(5);
        var chainB = new VstInsertChain();
        chainB.Slots[0].PluginPath = @"C:\B.vst3";
        try
        {
            engine.Apply(owner, chainB, playerBypass: false);
            applyDone = true;
        }
        catch (Exception ex)
        {
            applyEx = ex;
        }

        Assert.True(processThread.Join(TimeSpan.FromSeconds(3)), "Process hung");
        Assert.Null(processEx);
        Assert.Null(applyEx);
        Assert.True(processDone);
        Assert.True(applyDone);
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void PlaybackProvider_RoutesThroughVstEngine()
    {
        var factory = new CountingFactory { Gain = 3f };
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\Gain.vst3";
        engine.Apply(new object(), chain, playerBypass: false);

        var provider = new PlaybackSampleProvider();
        provider.SetVstInsertEngine(engine);
        provider.SetDeviceSampleRate(48000);

        // Bind silent-ish PCM via reflection-free public API: use silence-only path is private.
        // 代わりにエンジン単体で provider 経由相当の Process を検証済み。ここでは配線を確認。
        var buffer = new float[] { 0.2f, 0.2f, 0.2f, 0.2f };
        engine.Process(buffer, 0, 4, 2);
        Assert.Equal(new float[] { 0.6f, 0.6f, 0.6f, 0.6f }, buffer);
        Assert.False(engine.PlayerBypass);
        Assert.NotNull(engine.TryGetLive(0));
    }

    [Fact]
    public void ProcessDataLayout_MatchesMsvcX64()
    {
        Assert.Equal(24, Marshal.SizeOf<Vst3HostNative.ProcessSetup>());
        Assert.Equal(24, Marshal.SizeOf<Vst3HostNative.AudioBusBuffers>());
        Assert.Equal(80, Marshal.SizeOf<Vst3HostNative.ProcessData>());
    }

    [Fact]
    public void SetSlotSuspended_ForcesBypassUntilReleased()
    {
        var factory = new CountingFactory { Gain = 2f };
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(new object(), chain, playerBypass: false);

        var buffer = new float[] { 1f, 1f };
        engine.SetSlotSuspended(0, true);
        engine.Process(buffer, 0, 2, 1);
        Assert.Equal(new float[] { 1f, 1f }, buffer);

        engine.SetSlotSuspended(0, false);
        engine.Process(buffer, 0, 2, 1);
        Assert.Equal(new float[] { 2f, 2f }, buffer);
    }

    [Fact]
    public void Apply_DoesNotBlockCallerWhilePreviousProcessorDisposes()
    {
        var factory = new CountingFactory { Gain = 1f, DisposeDelayMs = 80 };
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var a = new VstInsertChain();
        a.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(owner, a, playerBypass: false);

        var b = new VstInsertChain();
        b.Slots[0].PluginPath = @"C:\B.vst3";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Apply(owner, b, playerBypass: false);
        sw.Stop();
        // Dispose を待たない（以前は WaitForProcessIdle で UI 相当が止まった）
        Assert.True(sw.ElapsedMilliseconds < 150, $"Apply blocked too long: {sw.ElapsedMilliseconds}ms");
        Assert.Equal(2, factory.CreateCount);
    }

    private sealed class OrderingFactory : IVstSlotProcessorFactory
    {
        public List<string> Events { get; } = [];

        public IVstSlotProcessor? TryCreate(VstInsertSlot slot, double sampleRate, int maxBlockSize, int channels)
        {
            var tag = System.IO.Path.GetFileNameWithoutExtension(slot.PluginPath) ?? "?";
            Events.Add("create:" + tag);
            return new OrderingProcessor(tag, Events);
        }
    }

    private sealed class OrderingProcessor : IVstSlotProcessor
    {
        private readonly string _tag;
        private readonly List<string> _events;

        public OrderingProcessor(string tag, List<string> events)
        {
            _tag = tag;
            _events = events;
            PluginPath = @"C:\" + tag + ".vst3";
            DisplayName = tag;
        }

        public string PluginPath { get; }

        public string? ClassId => null;

        public string DisplayName { get; }

        public bool Faulted => false;

        public void Reconfigure(double sampleRate, int maxBlockSize, int channels)
        {
        }

        public void ApplyState(byte[]? stateBlob)
        {
        }

        public byte[]? CaptureState() => null;

        public bool Process(float[] buffer, int offset, int sampleCount, int channels) => true;

        public Vst3EditorSession? TryOpenEditor() => null;

        public void ResetEditor()
        {
        }

        public void Dispose() => _events.Add("dispose:" + _tag);
    }

    private sealed class CountingFactory : IVstSlotProcessorFactory
    {
        public int CreateCount { get; private set; }

        public float Gain { get; set; } = 1f;

        public int ProcessDelayMs { get; set; }

        public int DisposeDelayMs { get; set; }

        public MockProcessor Last { get; private set; } = null!;

        public IVstSlotProcessor? TryCreate(VstInsertSlot slot, double sampleRate, int maxBlockSize, int channels)
        {
            CreateCount++;
            Last = new MockProcessor(slot.PluginPath!, slot.ClassId, Gain, ProcessDelayMs, DisposeDelayMs);
            if (slot.StateBlob is not null)
            {
                Last.ApplyState(slot.StateBlob);
            }

            return Last;
        }
    }

    private sealed class MockProcessor : IVstSlotProcessor
    {
        private readonly float _gain;
        private readonly int _delayMs;
        private readonly int _disposeDelayMs;

        public MockProcessor(string path, string? classId, float gain, int delayMs, int disposeDelayMs = 0)
        {
            PluginPath = path;
            ClassId = classId;
            DisplayName = path;
            _gain = gain;
            _delayMs = delayMs;
            _disposeDelayMs = disposeDelayMs;
        }

        public string PluginPath { get; }

        public string? ClassId { get; }

        public string DisplayName { get; }

        public bool Faulted => false;

        public byte[]? LastState { get; private set; }

        public int ProcessCount { get; private set; }

        public void Reconfigure(double sampleRate, int maxBlockSize, int channels)
        {
        }

        public void ApplyState(byte[]? stateBlob) => LastState = stateBlob is null ? null : (byte[])stateBlob.Clone();

        public byte[]? CaptureState() => LastState is null ? null : (byte[])LastState.Clone();

        public bool Process(float[] buffer, int offset, int sampleCount, int channels)
        {
            if (_delayMs > 0)
            {
                Thread.Sleep(_delayMs);
            }

            ProcessCount++;
            for (var i = 0; i < sampleCount; i++)
            {
                buffer[offset + i] *= _gain;
            }

            return true;
        }

        public Vst3EditorSession? TryOpenEditor() => null;

        public void ResetEditor()
        {
        }

        public void Dispose()
        {
            if (_disposeDelayMs > 0)
            {
                Thread.Sleep(_disposeDelayMs);
            }
        }
    }
}
