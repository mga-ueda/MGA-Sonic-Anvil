namespace MgaSonicAnvil.Audio;

internal sealed class Vst3SlotProcessorFactory : IVstSlotProcessorFactory
{
    public static Vst3SlotProcessorFactory Instance { get; } = new();

    public IVstSlotProcessor? TryCreate(VstInsertSlot slot, double sampleRate, int maxBlockSize, int channels) =>
        Vst3ProcessorInstance.TryCreate(slot, sampleRate, maxBlockSize, channels);
}
