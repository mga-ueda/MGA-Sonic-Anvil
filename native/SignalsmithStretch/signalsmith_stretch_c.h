#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#ifdef SIGNALSMITH_STRETCH_EXPORTS
#define SS_API __declspec(dllexport)
#else
#define SS_API __declspec(dllimport)
#endif

/// 0-1. May be called from the worker thread. Pass NULL to skip reports.
typedef void (*ss_progress_fn)(void *user, float progress);

/// Interleaved float PCM. time-preserving when output_frames == input_frames.
/// Returns 1 on success, 0 on failure.
SS_API int ss_pitch_exact_interleaved(
    const float *input,
    int input_frames,
    float *output,
    int output_frames,
    int channels,
    int sample_rate,
    float semitones,
    ss_progress_fn progress,
    void *user);

/// Streaming, time-preserving pitch. One instance per playback voice.
SS_API void *ss_rt_create(void);
SS_API void ss_rt_destroy(void *handle);
SS_API void ss_rt_set_semitones(void *handle, float semitones);
SS_API void ss_rt_reset(void *handle);
/// In-place interleaved float PCM. frame_offset is in frames. Returns 1 on success.
SS_API int ss_rt_process(
    void *handle,
    float *interleaved,
    int frame_offset,
    int frames,
    int channels,
    int sample_rate);

#ifdef __cplusplus
}
#endif
