# Third-party notices

This file is the attribution and license notice for third-party software
and training-data provenance bundled with or used by MGA Sonic Anvil.
The application itself is MIT-licensed; see [LICENSE](LICENSE).

---

## Wwise® / Audiokinetic® (trademarks)

Wwise® and Audiokinetic® are trademarks of Audiokinetic Inc.
This tool is unofficial. WAAPI requires a valid Wwise license.
Wwise Authoring and the Wwise SDK are **not** redistributed with this app.

---

## EffeTune Rhythm Analyzer DSP

**Use in this app:** Real-time BPM / beat analysis (Rhythm Analyzer DSP),
when that feature is enabled. Metronome click audio uses this app’s own
click samples, not EffeTune’s synthesized click.

**Source:** [Frieve-A/effetune](https://github.com/Frieve-A/effetune)  
**Local copy of the license text:** [native/effetune-rhythm/LICENSE-EffeTune.txt](native/effetune-rhythm/LICENSE-EffeTune.txt)

### MIT License (full text)

```
MIT License

Copyright (c) 2025-2026, Yoshiyuki Kobayashi

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Training-data attribution (provenance)

EffeTune ships frozen analysis weights. **Original training audio and labels
are not redistributed** with this app. The following attribution follows
EffeTune’s published provenance files for the Rhythm Analyzer.

#### Onset-lane models (CC BY 4.0 materials used for training)

These datasets are credited under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
Per EffeTune provenance: *“This material was used for training, calibration or
evaluation. The original material is not included.”*

| Dataset | Creators | Material | License |
|---------|----------|----------|---------|
| [Slakh2100-redux](https://www.slakh.com/) | Ethan Manilow, Gordon Wichern, Prem Seetharaman, Jonathan Le Roux | Mix audio and beat times from the MIDI tempo map | CC BY 4.0 |
| [Lakh MIDI Dataset v0.1](https://colinraffel.com/projects/lmd/) | Colin Raffel | MIDI underlying Slakh2100-redux renders | CC BY 4.0 |
| [Groove MIDI Dataset v1.0.0](https://magenta.tensorflow.org/datasets/groove) | Jon Gillick, Adam Roberts, Jesse Engel, Douglas Eck, David Bamman | Audio and performed beat times from MIDI | CC BY 4.0 |

Suggested citation forms (from EffeTune provenance):

- Manilow, Wichern, Seetharaman and Le Roux, “Cutting Music Source Separation Some Slakh: A Dataset to Study the Impact of Training Data Quality and Quantity”, IEEE WASPAA 2019.
- Colin Raffel, “Learning-Based Methods for Comparing Sequences, with Applications to Audio-to-MIDI Alignment and Matching”, PhD thesis, Columbia University, 2016.
- Gillick, Roberts, Engel, Eck and Bamman, “Learning to Groove with Inverse Sequence Transformations”, ICML 2019.

#### Tempo / beat network (T48M) training sets named by EffeTune

EffeTune’s `tc_tcn_weights.provenance.json` lists these training datasets
for the frozen T48M network (weights only; no dataset files are included here):

Ballroom; BRID; Candombe; Groove MIDI; GuitarSet; MusicNet public-domain
recordings; Salsa; Slakh2100-redux validation; Schubert Winterreise
public-domain recordings.

Dataset-specific terms may apply to how those corpora may be used for
training. This app redistributes only EffeTune’s MIT-licensed DSP and
frozen weights as published by EffeTune, not the corpora themselves.

---

## Signalsmith Stretch / Signalsmith Linear

**Use in this app:** Pitch shift.  
**Licenses:** MIT (copies under `native/SignalsmithStretch/` and `native/signalsmith-linear/`).

### Signalsmith Stretch — MIT License (full text)

```
MIT License

Copyright (c) 2022 Geraint Luff / Signalsmith Audio Ltd.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Signalsmith Linear is also MIT; see `native/signalsmith-linear/LICENSE.txt`.

---

## WinBlur

**Use in this app:** Implementation approach for `AcrylicBackdrop` (DWM
`SetWindowCompositionAttribute` / ACCENT Acrylic frosted glass), via the same
pattern used in MGA After Drive.

**Source:** [xauuzd/WinBlur](https://github.com/xauuzd/WinBlur)  
**Local copy of the license text:** [licenses/WinBlur-LICENSE.txt](licenses/WinBlur-LICENSE.txt)

### MIT License (full text)

```
MIT License

Copyright (c) 2022 Pronner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## LAME and FFmpeg (not bundled)

**LAME** and **FFmpeg** are **not** included in the application package. When
configured, the app launches a user-supplied `lame.exe` / `ffmpeg.exe` as a
separate process. Obtaining those tools and complying with their licenses is
the user’s responsibility.
