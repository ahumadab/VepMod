# VAD Integration — Developer Reference

Voice Activity Detection added to the Whispral Mimic voice recording pipeline.

> End-to-end overview of the whole voice system (capture → share → playback) with diagrams:
> [`voice-mimic-pipeline.md`](./voice-mimic-pipeline.md).

---

## Context

The Whispral enemy uses a Mimic system that records players' voices and replays them through
hallucination droids. Without filtering, any audio captured by the microphone — ambient noise,
game sounds, background noise — gets stored and replayed, breaking immersion.

The previous approach used simple energy thresholds (`AudioMinRms`, `AudioMinPeak`,
`AudioMinNonSilenceRatio`) to reject silent clips. These thresholds worked for obvious silence
but failed on real game sessions: quiet speech got rejected, and structured background noise
got accepted.

**Goal:** replace threshold-based filtering with a proper speech detector that can distinguish
human speech from environmental noise, regardless of amplitude.

---

## Approach

Three strategies were evaluated:

| Strategy | Description |
|---|---|
| **PreFilter (RMS thresholds)** | Keep the existing energy-based filter as a pre-pass |
| **VAD only** | WebRTC VAD — a GMM-based speech detector, frame-by-frame analysis |
| **Combined** | PreFilter (permissive) → VAD (strict) |

WebRTC VAD ([WebRtcVadSharp](https://www.nuget.org/packages/WebRtcVadSharp)) was chosen for
the VAD component: it is a lightweight C++ library (native x64 DLL) with a managed wrapper,
compatible with Unity runtime, and requires no external model files.

---

## Benchmark

### Dataset

- **248 WAV files** from 13 real players across multiple sessions
- Sample rates: 16 kHz, 44.1 kHz, 48 kHz
- Manual labeling via an interactive HTML tool (Speech / Noise / Unknown)
- Final ground truth after manual review: **180 Speech**, **68 Noise**
- Label source: `tests/AudioBenchmark/results/labels.json`

### Tool

`tests/AudioBenchmark/` — standalone .NET 8 console app.

```
tests/AudioBenchmark/
├── Program.cs                         # orchestrator: runs all configurations, writes CSV
├── Models/
│   ├── WavFile.cs
│   ├── BenchmarkResult.cs
│   └── LabeledResult.cs
├── Validators/
│   ├── VadAudioValidator.cs           # copy of production validator (standalone, no Unity deps)
│   ├── AudioRecordingValidator.cs     # RMS/Peak/NonSilence validator
│   ├── AudioAnalyzer.cs
│   └── WavReader.cs
├── Reports/
│   └── HtmlReportGenerator.cs        # interactive HTML report with Speech/Noise toggle buttons
└── results/
    ├── labels.json                    # ground truth labels (248 files)
    ├── benchmark_results.csv          # raw results for all 16 configurations
    └── labeling_report.html           # interactive labeling UI
```

To rebuild and run:

```sh
cd tests/AudioBenchmark
dotnet run -c Release
```

Output: `results/benchmark_<timestamp>.csv` and updated `results/labeling_report.html`.

### Configurations tested

16 combinations across:

- **PreFilter**: `Game` (original game thresholds), `Default` (relaxed RMS), disabled
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

**Key finding:** The combined pipeline (PreFilter + VAD) was the *worst* result.
`PreFilter.Game` rejected ~40% of valid speech recordings before VAD could see them,
destroying recall. Adding a permissive pre-filter did not help either — any rejection before
VAD becomes the bottleneck. The VAD alone with a low threshold outperforms all combinations.

**Performance:** ~0.63 ms/file on a mid-range CPU, safe for Unity runtime use.

---

## Production implementation

### Pipeline

```
VoiceRecorder.PushFrame (Photon voice thread)  →  Capture queued
WhispralMimics.SaveCaptures (main thread)      →  dequeues, then in a background task:
    └── CaptureValidation.Validate
            ├── duration guard      →  reject if < ConfigAudioMinDuration (default 0.3s)
            ├── trim trailing silence (analysis only — the Voice Sample stays full)
            └── SpeechCheck (VAD)   →  reject if speechRatio < threshold (sensitivity-dependent)
    └── VoiceBanks.Save, WavCodec.Encode  →  handed to sharing on the main thread
```

The VAD is the **sole content filter** — the previous RMS/Peak pre-filter was removed (the
benchmark showed it was net-negative: it rejected valid speech without improving precision over
accepting everything).

### Files

| File | Role |
|---|---|
| `src/VepFramework/Audio/VadAudioValidator.cs` | VAD wrapper — `VadValidationCriteria`, `VadSensitivity`, `FromSensitivity`, resampling, dispose lock |
| `src/Enemies/Whispral/VoiceRecorder.cs` | `VoiceRecorder` (voice thread) and `CaptureValidation` — trailing-silence trim, fail-open `try/catch` |
| `src/Enemies/Whispral/WhispralMimics.cs` | Drains the Captures, validates and saves them in a background task |
| `src/VepMod.cs` | Config entries (`ConfigVadEnabled`, `ConfigVadSensitivity`, `ConfigAudioMinDuration`), the one VAD handle |
| `VepMod.csproj` | Build config — WebRtcVadSharp 1.3.2, PlatformTarget x64, test exclusion |

`CombinedAudioValidator.cs`, `AudioRecordingValidator.cs` and `AudioAnalyzer.cs` were **deleted**
from `src/` (dead code once the pipeline became VAD-only). The benchmark keeps its own standalone
copies under `tests/AudioBenchmark/Validators/`.

### `VadSensitivity` presets

The recording strictness is selected by `VadSensitivity` (user config) and built by
`VadValidationCriteria.FromSensitivity(...)` in `VadAudioValidator.cs`. The three ratios map to
points measured by the benchmark, all in `OperatingMode.HighQuality`:

| Sensitivity | `MinSpeechRatio` | Recall | Precision |
|---|---|---|---|
| `Permissive` | 0.20 | 99% | 80% |
| `Balanced` (default) | 0.40 | 97% | 85% |
| `Strict` | 0.60 | 90% | 89% |

```csharp
public static VadValidationCriteria FromSensitivity(VadSensitivity sensitivity)
{
    var ratio = sensitivity switch
    {
        VadSensitivity.Permissive => 0.20f,
        VadSensitivity.Strict     => 0.60f,
        _                         => 0.40f // Balanced
    };
    return new VadValidationCriteria
    {
        MinDurationSeconds = 0f,                    // duration guard is upstream (CaptureValidation)
        MinSpeechRatio     = ratio,
        OperatingMode      = OperatingMode.HighQuality,
        FrameLength        = FrameLength.Is20ms,
        SampleRate         = SampleRate.Is16kHz
    };
}
```

`MinDurationSeconds = 0f` intentionally: `CaptureValidation.Validate` checks
`ConfigAudioMinDuration` before calling VAD, keeping the duration threshold user-configurable
without duplicating it inside the VAD criteria.

### Trailing-silence trim

A Capture ends with up to 0.5s of trailing silence, whose length it carries. That silence is trimmed
from the buffer passed to the VAD so it doesn't dilute the speech ratio and penalize short
utterances. The saved/shared Voice Sample keeps the full buffer — only the VAD analysis window is
trimmed. A 0.1s floor avoids degenerate analysis windows.

### WebRtcVadSharp — deployment

The NuGet package `WebRtcVadSharp 1.3.2` contains two DLLs, both deployed alongside `VepMod.dll`:

- `WebRtcVadSharp.dll` (11 KB) — managed .NET wrapper, copied to output by a `<Target>` in `VepMod.csproj` (netstandard2.1 does not copy NuGet runtime DLLs by default)
- `WebRtcVad.dll` (50 KB) — native C++ x64 library, copied to output by the NuGet package's own `.targets` file

The final plugin package contains 3 files:

```
plugins/
├── VepMod.dll
├── WebRtcVadSharp.dll
└── WebRtcVad.dll
```

Pinned at 1.3.2 — do not upgrade without re-running the benchmark, since the GMM model
behavior may change across versions.

### VAD instantiation — try/catch fallback

`VepMod.CreateSpeechCheck` builds the one VAD handle of the client at plugin load, when
`ConfigVadEnabled` is on, and logs it once. The sensitivity is read then, so changing it takes a
restart.

If `WebRtcVad.dll` (native x64) is absent or fails to load (e.g. on x86 or a broken install),
the constructor throws a `DllNotFoundException`. The catch leaves `VepMod.SpeechCheck` null and the
pipeline continues without speech filtering — Captures are accepted as-is, the mod still works.

There are **two** fail-open layers: this one at init, and a second `try/catch` in
`CaptureValidation.Validate` around the speech check (on exception the Capture is accepted). The
native VAD is also guarded by a lock so `Dispose()` (plugin `OnDestroy`, Unity thread) cannot free it
mid-`Validate()` in a background task.

---

## Build configuration (`VepMod.csproj`)

### `PlatformTarget x64`

```xml
<PlatformTarget>x64</PlatformTarget>
```

Required by `WebRtcVad.dll` (native x64 only). Without this, MSBuild emits a warning and
the native DLL may fail to load at runtime on 32-bit hosts.

### Test exclusion

```xml
<None Remove="tests\**"/>
```

`Linkoid.Repo.Plugin.Build` (the Thunderstore packaging tool) scans **all `None` items** in
the project and copies them into the plugin output directory. `EnableDefaultCompileItems=false`
only suppresses `Compile` items, not `None`. Without this exclusion, a local benchmark build
would pollute the plugin zip with `.exe`, `.dll`, `.csv`, and other test artifacts, making
the package non-deterministic.

### VAD-free build variant (`VepModNoVad`)

A build with `-p:VepModNoVad=true` produces a plugin with **no WebRTC dependency** — no
`WebRtcVadSharp` reference, no native `WebRtcVad.dll`, and all VAD code compiled out via the
`VEPMOD_NO_VAD` symbol (validator, `[Audio Quality]` VAD config, the pipeline branch, and the
dev tools). The recording pipeline keeps only the duration guard (`Min Duration`); every clip
that passes it is saved/shared. Use it to ship a lighter, dependency-free variant (e.g. for
hosts where the native x64 DLL cannot load).

```sh
dotnet build VepMod.csproj -c Debug -p:VepModNoVad=true   # or: ./build-dev.ps1 -NoVad
```

In `VepMod.csproj` the `WebRtcVadSharp` `PackageReference` and the `CopyWebRtcVadSharpManaged`
target are both `Condition="'$(VepModNoVad)' != 'true'"`. The default build (`false`) is unchanged.

---

## In-game testing (dev tools, DEBUG only)

`src/Dev/DroidDevTools.cs` (enabled via `Developer / Enable Dev Tools`) adds two VAD aids on top
of the droid keybinds, both compiled out in `VEPMOD_NO_VAD` builds:

- **F6 — replay `AudioFiles/` through the VAD.** Runs the production `VadAudioValidator` at the
  current `VAD Sensitivity` over every captured `.wav`, logging accept/reject + speech ratio per
  file and an aggregate. Mic-free, deterministic parity check vs the offline benchmark (whole-clip
  analysis, no trailing-silence trim).
- **HUD VAD line.** The F10 overlay shows the last recorded clip's verdict, ratio and the active
  sensitivity — immediate "penalizing or not" feedback while you talk in a solo lobby.

For the full end-to-end loop (share + playback on a hallucination) use two clients and trigger the
Whispral debuff; enable the BepInEx console to read the `Recording passed/rejected (VAD)` logs.

---

## User-facing configuration

Three entries in `[Audio Quality]` in `BepInEx/config/com.vep.vepMod.cfg`:

| Key | Default | Description |
|---|---|---|
| `Min Duration` | `0.3` | Reject recordings shorter than N seconds (pre-VAD guard) |
| `VAD Enabled` | `true` | Enable/disable WebRTC speech detection. Disable if `WebRtcVad.dll` is missing or fails to load (e.g. the file was not included in the mod package) |
| `VAD Sensitivity` | `Balanced` | Speech strictness: `Permissive` (0.20, keeps almost all voice), `Balanced` (0.40, best F1), `Strict` (0.60, filters more). Lower = less penalizing |

Removed configs (no longer exist): `AudioMinRms`, `AudioMinPeak`, `AudioMinNonSilenceRatio`.

---

## Final architecture

```
VepMod.Awake()
│
├── VoiceRecorder, SpeechCheck (new VadAudioValidator, once per client)   [try/catch]
│
LocalVoiceFramedPatch (Photon voice thread)
│     └── VoiceRecorder.PushFrame(isTalking, pcm, rate, channels) → Capture queued
│
WhispralMimics (MonoBehaviour, one per PlayerAvatar)
│
├── Awake()
│     └── StartCoroutine(WaitForVoiceChat)
│           └── ShareAudioLoop, SaveCaptures      [local player only]
│
├── Loop 1 — ShareAudioLoop (coroutine, local player only)
│     └── VoiceRecorder.RequestCapture()
│
├── SaveCaptures (coroutine, local player only)
│     ├── VoiceRecorder.TryTakeCapture()
│     ├── background task: CaptureValidation.Validate() [fail-open], VoiceBanks.Save(), WavCodec.Encode()
│     └── ShareAudioWithOthersAsync() → Photon RPC chunks to other players
│
├── RPC ReceiveSharedAudioChunk()     ← all clients
│     └── SaveReceivedAudioAsync() → VoiceBanks, under the sender's Steam ID
│
└── Loop 2 — PlayVoiceCommandRPC()    ← sent by Master via EnemyWhispral
      └── PlayAudioAtTransform() → AudioSource on hallucination droid
```
