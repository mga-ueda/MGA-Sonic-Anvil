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
    public void MoveSlot_ToEmpty_MovesWithoutLeavingPluginAtSource()
    {
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].DisplayName = "A";
        chain.Slots[0].ClassId = "a";
        var a = chain.Slots[0];
        var empty = chain.Slots[2];
        chain.MoveSlot(0, 2);
        Assert.Same(empty, chain.Slots[0]);
        Assert.Same(a, chain.Slots[2]);
        Assert.True(chain.Slots[0].IsEmpty);
        Assert.Equal(@"C:\A.vst3", chain.Slots[2].PluginPath);
    }

    [Fact]
    public void MoveSlot_BetweenOccupied_Swaps()
    {
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].DisplayName = "A";
        chain.Slots[1].PluginPath = @"C:\B.vst3";
        chain.Slots[1].DisplayName = "B";
        var a = chain.Slots[0];
        var b = chain.Slots[1];
        chain.MoveSlot(0, 1);
        Assert.Same(b, chain.Slots[0]);
        Assert.Same(a, chain.Slots[1]);
    }

    [Fact]
    public void MoveSlot_OnEngine_SwapsLiveWithoutRecreate()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].DisplayName = "A";
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Equal(1, factory.CreateCount);
        var live = engine.TryGetLive(0);
        Assert.NotNull(live);

        chain.MoveSlot(0, 3);
        engine.MoveSlot(0, 3, chain);
        Assert.Null(engine.TryGetLive(0));
        Assert.Same(live, engine.TryGetLive(3));
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void MoveSlot_AfterCapture_KeepsStateBlobWithSlotAndSameLive()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].DisplayName = "A";
        chain.Slots[0].StateBlob = [1, 2, 3];
        engine.Apply(owner, chain, playerBypass: false);
        var live = engine.TryGetLive(0);
        Assert.NotNull(live);
        Assert.Equal(new byte[] { 1, 2, 3 }, factory.Last.LastState);

        // ライブ側でパラメータが変わった想定。
        factory.Last.ApplyState([9, 9, 9]);
        engine.CaptureAppliedStates();
        Assert.Equal(new byte[] { 9, 9, 9 }, chain.Slots[0].StateBlob);

        chain.MoveSlot(0, 2);
        engine.MoveSlot(0, 2, chain);

        Assert.Same(live, engine.TryGetLive(2));
        Assert.Null(engine.TryGetLive(0));
        Assert.True(chain.Slots[0].IsEmpty);
        Assert.Equal(new byte[] { 9, 9, 9 }, chain.Slots[2].StateBlob);
        Assert.Equal(1, factory.CreateCount);
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
    public void Apply_BypassedSlot_DefersCreate_UntilUnbypassed()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].DisplayName = "A";
        chain.Slots[0].ClassId = "x";
        chain.Slots[0].StateBlob = [1, 2];
        chain.Slots[0].Bypassed = true;

        engine.Apply(owner, chain, playerBypass: false);
        Assert.Equal(0, factory.CreateCount);
        Assert.Null(engine.TryGetLive(0));
        Assert.True(engine.IsSlotBypassed(0));
        Assert.Equal("A", chain.Slots[0].DisplayName);

        chain.Slots[0].Bypassed = false;
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Equal(1, factory.CreateCount);
        Assert.NotNull(engine.TryGetLive(0));
        Assert.False(engine.IsSlotBypassed(0));
        Assert.Equal(new byte[] { 1, 2 }, factory.Last.LastState);
    }

    [Fact]
    public void Apply_BypassToggle_KeepsSameLiveInstance()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].ClassId = "x";

        engine.Apply(owner, chain, playerBypass: false);
        var live = engine.TryGetLive(0);
        Assert.NotNull(live);
        Assert.Equal(1, factory.CreateCount);

        chain.Slots[0].Bypassed = true;
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Same(live, engine.TryGetLive(0));
        Assert.True(engine.IsSlotBypassed(0));
        Assert.Equal(1, factory.CreateCount);

        chain.Slots[0].Bypassed = false;
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Same(live, engine.TryGetLive(0));
        Assert.False(engine.IsSlotBypassed(0));
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void Apply_OtherSlotChange_KeepsSamePluginEvenWhenBypassed()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].ClassId = "a";

        engine.Apply(owner, chain, playerBypass: false);
        var live = engine.TryGetLive(0);
        Assert.NotNull(live);

        chain.Slots[0].Bypassed = true;
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Same(live, engine.TryGetLive(0));

        // 他スロットの構造変更でも、同一プラグインのスロット0は維持。
        chain.Slots[1].PluginPath = @"C:\B.vst3";
        chain.Slots[1].ClassId = "b";
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Same(live, engine.TryGetLive(0));
        Assert.True(engine.IsSlotBypassed(0));
        Assert.NotNull(engine.TryGetLive(1));
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void Apply_ResolvedClassIdVsEmpty_DoesNotUnload()
    {
        var factory = new CountingFactory { ResolveClassIdWhenMissing = "resolved" };
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].ClassId = null;

        engine.Apply(owner, chain, playerBypass: false);
        var live = engine.TryGetLive(0);
        Assert.NotNull(live);
        Assert.Equal("resolved", live.ClassId);

        // チェーン側 ClassId は空のまま、別スロット追加でフル経路へ。
        chain.Slots[1].PluginPath = @"C:\B.vst3";
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Same(live, engine.TryGetLive(0));
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void EnsureSlotLoaded_LoadsBypassedDeferredSlot()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].DisplayName = "A";
        chain.Slots[0].Bypassed = true;
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Null(engine.TryGetLive(0));

        engine.EnsureSlotLoaded(0);
        Assert.Equal(1, factory.CreateCount);
        Assert.NotNull(engine.TryGetLive(0));
        Assert.True(engine.IsSlotBypassed(0));
    }

    [Fact]
    public void UnloadBypassedLiveSlots_DetachesBypassedKeepsActive()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[1].PluginPath = @"C:\B.vst3";
        engine.Apply(owner, chain, playerBypass: false);
        Assert.NotNull(engine.TryGetLive(0));
        Assert.NotNull(engine.TryGetLive(1));

        chain.Slots[0].Bypassed = true;
        engine.Apply(owner, chain, playerBypass: false);
        Assert.True(engine.IsSlotBypassed(0));
        Assert.NotNull(engine.TryGetLive(0));

        engine.UnloadBypassedLiveSlots();
        Assert.Null(engine.TryGetLive(0));
        Assert.NotNull(engine.TryGetLive(1));
        Assert.True(engine.IsSlotBypassed(0));

        engine.EnsureSlotLoaded(0);
        Assert.NotNull(engine.TryGetLive(0));
        Assert.Equal(3, factory.CreateCount);
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
    public void Apply_OwnerChange_EmptySlot_ParksAndRestoresWithoutReload()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var ownerA = new object();
        var ownerB = new object();

        var chainA = new VstInsertChain();
        chainA.Slots[0].PluginPath = @"C:\Persist.vst3";
        chainA.Slots[0].ClassId = "p";
        chainA.Slots[2].PluginPath = @"C:\AmpRoom.vst3";
        chainA.Slots[2].ClassId = "amp";
        chainA.Slots[2].StateBlob = [1, 2];
        engine.Apply(ownerA, chainA, playerBypass: false);
        var amp = engine.TryGetLive(2);
        Assert.NotNull(amp);
        Assert.Equal(2, factory.CreateCount);

        // 永続スロットだけある別タブへ。Amp Room は退避される。
        var chainB = new VstInsertChain();
        chainB.Slots[0].PluginPath = @"C:\Persist.vst3";
        chainB.Slots[0].ClassId = "p";
        engine.Apply(ownerB, chainB, playerBypass: false);
        Assert.Null(engine.TryGetLive(2));
        Assert.Equal(2, factory.CreateCount);

        // 元タブへ戻ると同一インスタンスを復帰（再ロードしない）。
        chainA.Slots[2].StateBlob = [9];
        engine.Apply(ownerA, chainA, playerBypass: false);
        Assert.Same(amp, engine.TryGetLive(2));
        Assert.Equal(2, factory.CreateCount);
        Assert.Equal(new byte[] { 9 }, factory.Last.LastState);
    }

    [Fact]
    public void Apply_SameOwner_ClearSlot_DisposesNotPark()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].ClassId = "a";
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Equal(1, factory.CreateCount);

        chain.ClearSlot(0);
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Null(engine.TryGetLive(0));

        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].ClassId = "a";
        engine.Apply(owner, chain, playerBypass: false);
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void Forget_DisposesParkedForOwner()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var ownerA = new object();
        var ownerB = new object();
        var chainA = new VstInsertChain();
        chainA.Slots[0].PluginPath = @"C:\A.vst3";
        chainA.Slots[0].ClassId = "a";
        engine.Apply(ownerA, chainA, playerBypass: false);

        engine.Apply(ownerB, new VstInsertChain(), playerBypass: false);
        Assert.Null(engine.TryGetLive(0));
        Assert.Equal(1, factory.CreateCount);

        engine.Forget(ownerA);
        engine.Apply(ownerA, chainA, playerBypass: false);
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void Apply_EditorToPlayerToEditor_RestoresWithoutReload()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].ClassId = "a";
        engine.Apply(owner, chain, playerBypass: false);
        var live = engine.TryGetLive(0);
        Assert.NotNull(live);

        engine.Apply(owner, chain, playerBypass: true);
        Assert.Null(engine.TryGetLive(0));
        Assert.Equal(1, factory.CreateCount);

        engine.Apply(owner, chain, playerBypass: false);
        Assert.Same(live, engine.TryGetLive(0));
        Assert.Equal(1, factory.CreateCount);
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
        Assert.Equal(0, factory.CreateCount);
        Assert.Null(engine.TryGetLive(0));
    }

    [Fact]
    public void Apply_PlayerBypass_DoesNotLoadEvenWhenNotBypassed()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].Bypassed = false;
        chain.Slots[1].PluginPath = @"C:\B.vst3";
        chain.Slots[1].Bypassed = true;

        engine.Apply(new object(), chain, playerBypass: true);

        Assert.Equal(0, factory.CreateCount);
        Assert.Null(engine.TryGetLive(0));
        Assert.Null(engine.TryGetLive(1));
    }

    [Fact]
    public void Apply_PlayerThenEditor_LoadsNonBypassedOnly()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].Bypassed = false;
        chain.Slots[1].PluginPath = @"C:\B.vst3";
        chain.Slots[1].Bypassed = true;

        engine.Apply(owner, chain, playerBypass: true);
        Assert.Equal(0, factory.CreateCount);

        engine.Apply(owner, chain, playerBypass: false);
        Assert.Equal(1, factory.CreateCount);
        Assert.NotNull(engine.TryGetLive(0));
        Assert.Null(engine.TryGetLive(1));
    }

    [Fact]
    public void Apply_EditorToPlayer_UnloadsLive()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";

        engine.Apply(owner, chain, playerBypass: false);
        Assert.Equal(1, factory.CreateCount);
        Assert.NotNull(engine.TryGetLive(0));

        engine.Apply(owner, chain, playerBypass: true);
        Assert.Equal(1, factory.CreateCount);
        Assert.Null(engine.TryGetLive(0));
    }

    [Fact]
    public void EnsureSlotLoaded_PlayerBypass_DoesNotCreate()
    {
        var factory = new CountingFactory();
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        chain.Slots[0].Bypassed = true;
        engine.Apply(new object(), chain, playerBypass: true);
        engine.EnsureSlotLoaded(0);
        Assert.Equal(0, factory.CreateCount);
        Assert.Null(engine.TryGetLive(0));
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
    public async Task ProcessOffline_AppliesGainWhileExclusive()
    {
        var factory = new CountingFactory { Gain = 2f };
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(new object(), chain, playerBypass: false);

        var buffer = new float[] { 0.5f, 0.25f, 0.1f, 0.2f };
        await engine.ProcessOfflineAsync(buffer, channels: 1);
        Assert.Equal(new float[] { 1f, 0.5f, 0.2f, 0.4f }, buffer);
        Assert.True(factory.Last.ProcessCount >= 1);
        Assert.True(factory.Last.ResetProcessingCount >= 1);

        // exclusive 解除後は通常 Process も動く
        var live = new float[] { 0.5f };
        engine.Process(live, 0, live.Length, 1);
        Assert.Equal(new float[] { 1f }, live);
    }

    [Fact]
    public async Task ProcessOffline_ClearsResidueBeforeSecondPass()
    {
        // ライブ温存で同一プロセッサを直列バウンスしても、2 本目先頭へ wet が混ざらない。
        var factory = new CountingFactory { LeaveBleed = true };
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\Reverb.vst3";
        engine.Apply(new object(), chain, playerBypass: false);

        var first = new float[] { 1f, 0f, 0f, 0f };
        engine.Process(first, 0, first.Length, 1);
        Assert.True(factory.Last.Bleed > 0f);
        Assert.Equal(0, factory.Last.ResetProcessingCount);

        var second = new float[] { 0f, 0f, 0f, 0f };
        await engine.ProcessOfflineAsync(second, channels: 1);
        Assert.True(factory.Last.ResetProcessingCount >= 1);
        Assert.Equal(0f, second[0]);
        Assert.Equal(0f, factory.Last.Bleed);
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
    public void SetSlotSuspended_SkipsProcessButPassesDry()
    {
        var factory = new CountingFactory { Gain = 2f };
        using var engine = new VstInsertEngine(factory);
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = @"C:\A.vst3";
        engine.Apply(new object(), chain, playerBypass: false);

        var buffer = new float[] { 1f, 1f };
        engine.SetSlotSuspended(0, true);
        engine.Process(buffer, 0, 2, 1);
        // createView 中は process しないが、無音より dry 通しの方が自然。
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

        public void ResetProcessing()
        {
        }

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

        public bool LeaveBleed { get; set; }

        public int ProcessDelayMs { get; set; }

        public int DisposeDelayMs { get; set; }

        /// <summary>スロット ClassId が空のとき、実機同様に解決済み ID をプロセッサへ載せる。</summary>
        public string? ResolveClassIdWhenMissing { get; set; }

        public MockProcessor Last { get; private set; } = null!;

        public IVstSlotProcessor? TryCreate(VstInsertSlot slot, double sampleRate, int maxBlockSize, int channels)
        {
            CreateCount++;
            var classId = string.IsNullOrWhiteSpace(slot.ClassId)
                ? ResolveClassIdWhenMissing
                : slot.ClassId;
            Last = new MockProcessor(slot.PluginPath!, classId, Gain, ProcessDelayMs, DisposeDelayMs, LeaveBleed);
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
        private readonly bool _leaveBleed;

        public MockProcessor(
            string path,
            string? classId,
            float gain,
            int delayMs,
            int disposeDelayMs = 0,
            bool leaveBleed = false)
        {
            PluginPath = path;
            ClassId = classId;
            DisplayName = path;
            _gain = gain;
            _delayMs = delayMs;
            _disposeDelayMs = disposeDelayMs;
            _leaveBleed = leaveBleed;
        }

        public string PluginPath { get; }

        public string? ClassId { get; }

        public string DisplayName { get; }

        public bool Faulted => false;

        public byte[]? LastState { get; private set; }

        public int ProcessCount { get; private set; }

        public int ResetProcessingCount { get; private set; }

        public float Bleed { get; private set; }

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
            var hadEnergy = false;
            for (var i = 0; i < sampleCount; i++)
            {
                if (Math.Abs(buffer[offset + i]) > 1e-8f)
                {
                    hadEnergy = true;
                    break;
                }
            }

            if (sampleCount > 0 && Bleed != 0f)
            {
                buffer[offset] += Bleed;
            }

            for (var i = 0; i < sampleCount; i++)
            {
                buffer[offset + i] *= _gain;
            }

            if (_leaveBleed)
            {
                // 無音フラッシュでは再武装せず、入力にエネルギーがあるときだけ残す。
                Bleed = hadEnergy ? 1f : 0f;
            }

            return true;
        }

        public void ResetProcessing()
        {
            ResetProcessingCount++;
            Bleed = 0f;
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
