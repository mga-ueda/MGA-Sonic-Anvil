#include "signalsmith_stretch_c.h"

#include "signalsmith-stretch.h"

#include <algorithm>
#include <cmath>
#include <vector>

using Stretch = signalsmith::stretch::SignalsmithStretch<float>;

namespace {

struct OffsetChannels
{
    float **data;
    int offset;

    float *operator[](int channel) const
    {
        return data[static_cast<size_t>(channel)] + offset;
    }
};

void ReportProgress(ss_progress_fn progress, void *user, float value, int &lastPercent)
{
    if (progress == nullptr)
    {
        return;
    }

    const auto percent = static_cast<int>(std::lround(std::clamp(value, 0.f, 1.f) * 100.f));
    if (percent == lastPercent)
    {
        return;
    }

    lastPercent = percent;
    progress(user, static_cast<float>(percent) / 100.f);
}

} // namespace

int ss_pitch_exact_interleaved(
    const float *input,
    int input_frames,
    float *output,
    int output_frames,
    int channels,
    int sample_rate,
    float semitones,
    ss_progress_fn progress,
    void *user)
{
    if (input == nullptr || output == nullptr
        || input_frames < 1 || output_frames < 1
        || channels < 1 || sample_rate < 1)
    {
        return 0;
    }

    int lastPercent = -1;
    ReportProgress(progress, user, 0.f, lastPercent);

    Stretch stretch(1);
    stretch.presetDefault(channels, static_cast<float>(sample_rate));
    stretch.setTransposeSemitones(semitones, 8000.f / static_cast<float>(sample_rate));

    const float rate = static_cast<float>(input_frames) / static_cast<float>(output_frames);
    auto seek = stretch.outputSeekLength(rate);
    auto padded_in = input_frames;
    auto padded_out = output_frames;
    if (input_frames < seek)
    {
        padded_in = seek;
        padded_out = std::max(1, static_cast<int>(std::lround(static_cast<double>(padded_in) / rate)));
    }

    std::vector<std::vector<float>> in_ch(static_cast<size_t>(channels));
    std::vector<std::vector<float>> out_ch(static_cast<size_t>(channels));
    std::vector<float *> in_ptr(static_cast<size_t>(channels));
    std::vector<float *> out_ptr(static_cast<size_t>(channels));
    for (int c = 0; c < channels; c++)
    {
        in_ch[static_cast<size_t>(c)].assign(static_cast<size_t>(padded_in), 0.f);
        out_ch[static_cast<size_t>(c)].assign(static_cast<size_t>(padded_out), 0.f);
        for (int i = 0; i < input_frames; i++)
        {
            in_ch[static_cast<size_t>(c)][static_cast<size_t>(i)] = input[i * channels + c];
        }

        in_ptr[static_cast<size_t>(c)] = in_ch[static_cast<size_t>(c)].data();
        out_ptr[static_cast<size_t>(c)] = out_ch[static_cast<size_t>(c)].data();
        ReportProgress(progress, user, 0.04f * static_cast<float>(c + 1) / static_cast<float>(channels), lastPercent);
    }

    float **inputs = in_ptr.data();
    float **outputs = out_ptr.data();
    const float playbackRate = static_cast<float>(padded_in) / static_cast<float>(padded_out);
    const int seekLength = stretch.outputSeekLength(playbackRate);
    if (padded_in < seekLength)
    {
        return 0;
    }

    stretch.outputSeek(inputs, seekLength);
    ReportProgress(progress, user, 0.08f, lastPercent);

    const int outputIndex = padded_out - static_cast<int>(seekLength / playbackRate);
    const int inputRemain = padded_in - seekLength;
    const int hop = std::max(stretch.intervalSamples(), sample_rate / 4);
    int inDone = 0;
    int outDone = 0;
    while (outDone < outputIndex)
    {
        const int outLeft = outputIndex - outDone;
        const int outChunk = std::min(hop, outLeft);
        const int inLeft = inputRemain - inDone;
        const int inChunk = outChunk == outLeft
            ? inLeft
            : std::clamp(
                static_cast<int>(std::lround(
                    static_cast<double>(outDone + outChunk) * inputRemain / std::max(1, outputIndex))) - inDone,
                0,
                inLeft);
        OffsetChannels inOff{inputs, seekLength + inDone};
        OffsetChannels outOff{outputs, outDone};
        stretch.process(inOff, inChunk, outOff, outChunk);
        inDone += inChunk;
        outDone += outChunk;
        const float processT = outputIndex <= 0 ? 1.f : static_cast<float>(outDone) / static_cast<float>(outputIndex);
        ReportProgress(progress, user, 0.08f + 0.84f * processT, lastPercent);
    }

    OffsetChannels flushOut{outputs, outputIndex};
    stretch.flush(flushOut, padded_out - outputIndex, playbackRate);
    ReportProgress(progress, user, 0.94f, lastPercent);

    const auto copy_frames = std::min(output_frames, padded_out);
    for (int i = 0; i < copy_frames; i++)
    {
        for (int c = 0; c < channels; c++)
        {
            output[i * channels + c] = out_ch[static_cast<size_t>(c)][static_cast<size_t>(i)];
        }

        if (((i + 1) & 4095) == 0)
        {
            ReportProgress(
                progress,
                user,
                0.94f + 0.06f * static_cast<float>(i + 1) / static_cast<float>(copy_frames),
                lastPercent);
        }
    }

    for (int i = copy_frames * channels; i < output_frames * channels; i++)
    {
        output[i] = 0;
    }

    ReportProgress(progress, user, 1.f, lastPercent);
    return 1;
}
