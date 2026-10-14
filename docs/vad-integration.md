# VAD Integration — Developer Reference

Why Voice Samples are filtered with WebRTC VAD, how the thresholds were chosen, and what the code cannot show about it.
ADR-0004 records the decision; [`voice-mimic-pipeline.md`](./voice-mimic-pipeline.md) shows where the VAD sits in the
voice system.

---

## Context

Without filtering, anything the microphone picks up (ambient noise, game sounds) becomes a Voice Sample and is replayed
by a Mimic, which breaks immersion.

The first filter used energy thresholds (`AudioMinRms`, `AudioMinPeak`, `AudioMinNonSilenceRatio`). They caught obvious
silence but failed on real sessions: quiet speech was rejected and structured background noise accepted.

**Goal:** a speech detector that tells human speech from environmental noise, whatever the amplitude.

---

## Approach

Three strategies were evaluated:

| Strategy | Description |
|---|---|
| **PreFilter (RMS thresholds)** | Keep the energy-based filter as a pre-pass |
| **VAD only** | WebRTC VAD, a GMM-based speech detector, frame by frame |
| **Combined** | PreFilter (permissive) → VAD (strict) |

WebRTC VAD ([WebRtcVadSharp](https://www.nuget.org/packages/WebRtcVadSharp)) was chosen: a lightweight native x64 C++
library with a managed wrapper, usable in the Unity runtime, with no model files to ship.

---

## Benchmark

### Dataset

- **248 WAV files** from 13 real players across several sessions, at 16 kHz, 44.1 kHz and 48 kHz
- Labelled by hand in an interactive HTML tool (Speech / Noise / Unknown)
- Ground truth after review: **180 Speech**, **68 Noise**, in `tests/AudioBenchmark/results/labels.json`

### Tool

`tests/AudioBenchmark/` is a standalone .NET 8 console app. It keeps its own copies of the validators, including the
energy-threshold one that production dropped. Run it with `dotnet run -c Release` from that folder; it writes
`results/benchmark_<timestamp>.csv` and refreshes `results/labeling_report.html`.

### Configurations tested

16 combinations across:

- **PreFilter**: `Game` (the original thresholds), `Default` (relaxed RMS), disabled
- **VAD**: `Default` (speechRatio ≥ 0.10, HighQuality), `Strict` (speechRatio ≥ 0.60, Aggressive)
- **SpeechRatio thresholds**: 0.10, 0.20, 0.40, 0.60

### Key results

| Configuration | Precision | Recall | F1 |
|---|---|---|---|
| **VAD only, speechRatio ≥ 0.40, HighQuality** | **85%** | **97%** | **90%** |
| VAD only, speechRatio ≥ 0.20, HighQuality | 80% | 99% | 89% |
| VAD only, speechRatio ≥ 0.60, HighQuality | 89% | 90% | 89% |
| PreFilter Game + VAD 0.40 (Combined) | 79% | 60% | 67% |
| PreFilter only (Game thresholds) | 71% | 82% | 76% |

**Winner:** VAD only, `speechRatio >= 0.40`, `OperatingMode.HighQuality`.

**Key finding:** the combined pipeline was the *worst*. `PreFilter.Game` rejected ~40% of valid speech before the VAD
saw it. A permissive pre-filter did not help either: any rejection before the VAD becomes the bottleneck. So the VAD is
the only content filter in production, behind a plain duration guard (`Min Duration`).

**Performance:** ~0.63 ms per file on a mid-range CPU.

The `VAD Sensitivity` presets are three of these points: `Permissive` 0.20, `Balanced` 0.40 (default, best F1),
`Strict` 0.60, all in `HighQuality`.

---

## What the code cannot show

### Trailing silence

A Capture ends after half a second of silence, and that silence stays in the Voice Sample. The VAD analyses the Capture
without it: it would dilute the speech ratio and penalise short words. The benchmark analysed whole files, so the
dev tools' F6 replay (whole files too) matches the benchmark, not production.

### Fail-open, twice

If `WebRtcVad.dll` is absent or fails to load (x86, broken install), creating the VAD throws and Captures are accepted
without a speech check. If the VAD throws while checking a Capture, the Capture is accepted too. Losing a Voice Sample
to a native hiccup is worse than keeping some noise (ADR-0004).

The VAD handle is created once per client, at plugin load: changing `VAD Sensitivity` takes a restart.

### Packaging

`WebRtcVadSharp` is pinned at 1.3.2: re-run the benchmark before upgrading, since the GMM model can change across
versions. The plugin ships three files: `VepMod.dll`, the managed `WebRtcVadSharp.dll` (copied by a target in
`VepMod.csproj`, as netstandard2.1 does not copy NuGet runtime DLLs) and the native `WebRtcVad.dll` (copied by the
package's own `.targets`).

`./build-dev.ps1 -NoVad` (or `-p:VepModNoVad=true`) builds the variant without any of it, for hosts where the native DLL
cannot load: Captures then pass on the duration guard alone.
