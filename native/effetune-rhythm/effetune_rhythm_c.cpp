#include "effetune_rhythm_c.h"

#include "rd6_engine.h"
#include "rhythm_d.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <limits>
#include <memory>
#include <new>

namespace {

using effetune::plugins::analyzer::rhythm_a3::BeatEvent;
using effetune::plugins::analyzer::rhythm_a3::Rd6Engine;
using effetune::plugins::analyzer::rhythm_d::Detector;
using effetune::plugins::analyzer::rhythm_d::RateRule;
using effetune::plugins::analyzer::rhythm_d::portableCos;
using effetune::plugins::analyzer::rhythm_d::portableExp;
using effetune::plugins::analyzer::rhythm_d::portableSin;

template <class T, std::uint32_t N> struct Queue {
  std::array<T, N> data{};
  std::uint32_t read = 0u, count = 0u;
  void clear() noexcept { read = count = 0u; }
  bool push(const T &value) noexcept {
    if (count == N)
      return false;
    data[(read + count++) % N] = value;
    return true;
  }
  T &front() noexcept { return data[read]; }
  void pop() noexcept {
    read = (read + 1u) % N;
    --count;
  }
};

constexpr std::uint32_t kIoiCount = 8u;

struct Analyzer {
  Detector lanes;
  std::unique_ptr<Rd6Engine> beat;
  Queue<std::uint64_t, 32u> clicks;
  std::array<double, kIoiCount> ioi_ms{};
  double host_rate = 48000.0;
  double click_a = 0.0, click_b = 0.0, click_start = 0.0, click_y1 = 0.0, click_y2 = 0.0;
  float ioi_bpm = 0.0f;
  std::uint32_t analysis_rate = 0u;
  std::uint32_t analysed = 0u;
  std::uint32_t click_attack = 1u, click_length = 0u, click_age = 0u;
  std::uint32_t ioi_write = 0u, ioi_filled = 0u;
  std::uint64_t input = 0u;
  std::uint64_t last_beat_sample = (std::numeric_limits<std::uint64_t>::max)();
  bool ready = false;
  bool click_on = false;
  bool ioi_valid = false;
};

double Bounded(double value, double lo, double hi, double fallback) noexcept {
  if (!std::isfinite(value)) {
    return fallback;
  }
  return value < lo ? lo : value > hi ? hi : value;
}

void ClearIoi(Analyzer &self) noexcept {
  self.ioi_write = 0u;
  self.ioi_filled = 0u;
  self.ioi_valid = false;
  self.ioi_bpm = 0.0f;
  self.last_beat_sample = (std::numeric_limits<std::uint64_t>::max)();
  self.ioi_ms.fill(0.0);
}

void ClearClick(Analyzer &self) noexcept {
  self.clicks.clear();
  self.click_age = self.click_length;
  self.click_y1 = self.click_y2 = 0.0;
}

void PrepareClickSynth(Analyzer &self) noexcept {
  constexpr double pi = 3.141592653589793;
  const double omega = 2.0 * pi * 2000.0 / self.host_rate;
  const double r = portableExp(-1.0 / (.006 * self.host_rate));
  const double amplitude = .31622776601683794 * portableExp(.0005 / .006);
  self.click_a = 2.0 * r * portableCos(omega);
  self.click_b = r * r;
  self.click_start = -amplitude * portableSin(omega) / r;
  self.click_attack = static_cast<std::uint32_t>(std::ceil(.0005 * self.host_rate));
  self.click_length = static_cast<std::uint32_t>(std::ceil(.06 * self.host_rate));
  ClearClick(self);
}

void RefreshIoiBpm(Analyzer &self) noexcept {
  if (self.ioi_filled == 0u) {
    self.ioi_valid = false;
    self.ioi_bpm = 0.0f;
    return;
  }

  std::array<double, kIoiCount> sorted{};
  for (std::uint32_t i = 0u; i < self.ioi_filled; ++i) {
    sorted[i] = self.ioi_ms[i];
  }
  std::sort(sorted.begin(), sorted.begin() + static_cast<std::ptrdiff_t>(self.ioi_filled));
  const double median = self.ioi_filled % 2u == 0u
                            ? 0.5 * (sorted[self.ioi_filled / 2u - 1u] + sorted[self.ioi_filled / 2u])
                            : sorted[self.ioi_filled / 2u];
  if (!(median > 0.0) || !std::isfinite(median)) {
    self.ioi_valid = false;
    self.ioi_bpm = 0.0f;
    return;
  }

  const double bpm = 60000.0 / median;
  if (!(bpm >= 40.0) || !(bpm <= 240.0) || !std::isfinite(bpm)) {
    self.ioi_valid = false;
    self.ioi_bpm = 0.0f;
    return;
  }

  self.ioi_bpm = static_cast<float>(bpm);
  self.ioi_valid = true;
}

void RecordBeat(Analyzer &self, std::uint64_t sample) noexcept {
  if (self.last_beat_sample != (std::numeric_limits<std::uint64_t>::max)()
      && sample > self.last_beat_sample && self.host_rate > 0.0) {
    const double ms = 1000.0 * static_cast<double>(sample - self.last_beat_sample) / self.host_rate;
    // 40..240 BPM の間隔だけ採用。
    if (ms >= 60000.0 / 240.0 && ms <= 60000.0 / 40.0 && std::isfinite(ms)) {
      self.ioi_ms[self.ioi_write] = ms;
      self.ioi_write = (self.ioi_write + 1u) % kIoiCount;
      if (self.ioi_filled < kIoiCount) {
        ++self.ioi_filled;
      }
      RefreshIoiBpm(self);
    }
  }

  self.last_beat_sample = sample;
}

void OnForwardBeat(void *context, const BeatEvent &beat) noexcept {
  auto &self = *static_cast<Analyzer *>(context);
  if (beat.time < 0.0 || !beat.shown) {
    return;
  }
  auto target = static_cast<std::uint64_t>(std::ceil(beat.time * self.host_rate));
  if (target < self.input) {
    target = self.input;
  }
  // 表示用 BPM はクリック有無に関わらず拍間隔から取る。
  RecordBeat(self, target);
  if (self.click_on) {
    self.clicks.push(target);
  }
}

void FeedMono(Analyzer &self, float mono, std::uint32_t frames_left_in_block) noexcept {
  self.lanes.push(mono);
  if (!self.beat) {
    return;
  }
  while (self.analysed != self.lanes.analysisSamples()) {
    const float sample = self.lanes.sampleAt(self.analysed++);
    constexpr float silence = 1.0e-30f;
    const float beat_sample =
        sample >= -silence && sample <= silence ? 0.0f : sample;
    const double availability =
        static_cast<double>(self.input + frames_left_in_block) / self.host_rate;
    self.beat->sample(beat_sample, availability);
  }
}

void MixClickSample(Analyzer &self, float *interleaved, int frame, int channels) noexcept {
  if (self.click_on && self.clicks.count != 0u && self.clicks.front() <= self.input) {
    self.clicks.pop();
    self.click_age = 0u;
    self.click_y1 = 0.0;
    self.click_y2 = self.click_start;
  }
  if (self.click_age >= self.click_length) {
    return;
  }
  const double y = self.click_a * self.click_y1 - self.click_b * self.click_y2;
  self.click_y2 = self.click_y1;
  self.click_y1 = y;
  const double gain =
      self.click_age < self.click_attack
          ? static_cast<double>(self.click_age) / self.click_attack
          : 1.0;
  ++self.click_age;
  const float click = static_cast<float>(gain * y);
  const int base = frame * channels;
  interleaved[base] += click;
  if (channels > 1) {
    interleaved[base + 1] += click;
  }
}

} // namespace

extern "C" void *er_create(void) {
  try {
    return new Analyzer();
  } catch (...) {
    return nullptr;
  }
}

extern "C" void er_destroy(void *handle) {
  delete static_cast<Analyzer *>(handle);
}

extern "C" int er_prepare(void *handle, double sample_rate) {
  auto *self = static_cast<Analyzer *>(handle);
  if (self == nullptr) {
    return 0;
  }

  self->ready = false;
  self->host_rate =
      std::isfinite(sample_rate) && sample_rate > 0.0 ? sample_rate : 48000.0;
  self->analysis_rate = RateRule::analysisRate(self->host_rate);
  self->lanes.prepare(self->host_rate);
  if (self->analysis_rate == 0u) {
    self->beat.reset();
    return 0;
  }

  if (!self->beat) {
    self->beat = std::make_unique<Rd6Engine>();
  }
  self->beat->sink = {self, &OnForwardBeat, nullptr, nullptr, nullptr};
  self->beat->prepare(self->analysis_rate, self->host_rate);
  PrepareClickSynth(*self);
  self->analysed = 0u;
  self->input = 0u;
  self->ready = true;
  self->lanes.reset();
  self->beat->reset();
  ClearClick(*self);
  ClearIoi(*self);
  return 1;
}

extern "C" void er_reset(void *handle) {
  auto *self = static_cast<Analyzer *>(handle);
  if (self == nullptr || !self->ready) {
    return;
  }
  self->lanes.reset();
  if (self->beat) {
    self->beat->reset();
  }
  self->analysed = 0u;
  self->input = 0u;
  ClearClick(*self);
  ClearIoi(*self);
}

extern "C" void er_set_range(void *handle, double minimum_bpm, double maximum_bpm) {
  auto *self = static_cast<Analyzer *>(handle);
  if (self == nullptr || !self->beat) {
    return;
  }
  double minimum = Bounded(minimum_bpm, 40.0, 192.0, 40.0);
  double maximum = Bounded(maximum_bpm, 50.0, 240.0, 240.0);
  if (maximum < minimum * 1.25) {
    maximum = minimum * 1.25;
  }
  self->beat->setRange(minimum, maximum);
}

extern "C" void er_set_click(void *handle, int enabled) {
  auto *self = static_cast<Analyzer *>(handle);
  if (self == nullptr) {
    return;
  }
  const bool on = enabled != 0;
  if (!on && self->click_on) {
    ClearClick(*self);
  }
  self->click_on = on;
}

extern "C" void er_process_mono(void *handle, const float *samples, int count) {
  auto *self = static_cast<Analyzer *>(handle);
  if (self == nullptr || !self->ready || self->beat == nullptr || samples == nullptr
      || count <= 0) {
    return;
  }

  for (int i = 0; i < count; ++i) {
    const float x = std::isfinite(samples[i]) ? samples[i] : 0.0f;
    FeedMono(*self, x, static_cast<std::uint32_t>(count - i));
    ++self->input;
  }
}

extern "C" void er_process_interleaved(void *handle, float *interleaved, int frames,
                                       int channels) {
  auto *self = static_cast<Analyzer *>(handle);
  if (self == nullptr || !self->ready || self->beat == nullptr || interleaved == nullptr
      || frames <= 0 || channels < 1) {
    return;
  }

  for (int i = 0; i < frames; ++i) {
    const int base = i * channels;
    const float left = std::isfinite(interleaved[base]) ? interleaved[base] : 0.0f;
    const float right = channels == 1
                            ? left
                            : (std::isfinite(interleaved[base + 1]) ? interleaved[base + 1] : 0.0f);
    FeedMono(*self, 0.5f * (left + right), static_cast<std::uint32_t>(frames - i));
    MixClickSample(*self, interleaved, i, channels);
    ++self->input;
  }
}

extern "C" int er_get_bpm(void *handle, float *out_bpm, float *out_confidence) {
  auto *self = static_cast<Analyzer *>(handle);
  if (self == nullptr || !self->ready || self->beat == nullptr) {
    return 0;
  }
  if (!self->beat->shown()) {
    return 0;
  }

  float column[192];
  float bpm = 0.0f;
  self->beat->tempogram(column, bpm);
  if (!(bpm > 0.0f) || !std::isfinite(bpm)) {
    const auto &anchor = self->beat->anchor();
    if (anchor.period > 0.0) {
      bpm = static_cast<float>(60.0 / anchor.period);
    }
  }
  if (!(bpm > 0.0f) || !std::isfinite(bpm)) {
    return 0;
  }

  if (out_bpm != nullptr) {
    *out_bpm = bpm;
  }
  if (out_confidence != nullptr) {
    *out_confidence = static_cast<float>(std::clamp(self->beat->confidence(), 0.0, 1.0));
  }
  return 1;
}

extern "C" int er_get_ioi_bpm(void *handle, float *out_bpm) {
  auto *self = static_cast<Analyzer *>(handle);
  if (self == nullptr || !self->ready || !self->ioi_valid) {
    return 0;
  }
  if (out_bpm != nullptr) {
    *out_bpm = self->ioi_bpm;
  }
  return 1;
}
