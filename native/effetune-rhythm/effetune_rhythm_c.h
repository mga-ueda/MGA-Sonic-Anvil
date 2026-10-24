#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#ifdef EFFETUNE_RHYTHM_EXPORTS
#define ER_API __declspec(dllexport)
#else
#define ER_API __declspec(dllimport)
#endif

/// Opaque analyzer handle.
ER_API void *er_create(void);
ER_API void er_destroy(void *handle);

/// Prepare for host sample rate. Returns 1 on success, 0 if rate unsupported.
ER_API int er_prepare(void *handle, double sample_rate);

ER_API void er_reset(void *handle);

/// Tempo search range. Defaults 40..240.
ER_API void er_set_range(void *handle, double minimum_bpm, double maximum_bpm);

/// Metronome click mix into the interleaved buffer. 0 off, non-zero on.
/// Beat times are always tracked for IOI BPM even when click mix is off.
ER_API void er_set_click(void *handle, int enabled);

/// Feed mono host-rate samples (analysis only; does not modify the buffer).
ER_API void er_process_mono(void *handle, const float *samples, int count);

/// Analyze interleaved float PCM and optionally mix metronome click into ch 0/1.
/// Layout: frame-major interleaved (L,R,L,R,...).
ER_API void er_process_interleaved(void *handle, float *interleaved, int frames, int channels);

/// EffeTune period_/tempogram BPM (may lock at half/double). Analysis only.
ER_API int er_get_bpm(void *handle, float *out_bpm, float *out_confidence);

/// Display BPM from median inter-beat interval: 60000 / ms. Returns 1 when valid.
ER_API int er_get_ioi_bpm(void *handle, float *out_bpm);

#ifdef __cplusplus
}
#endif
