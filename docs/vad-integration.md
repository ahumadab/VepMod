# VAD Integration — Developer Reference

Voice Activity Detection added to the Whispral Mimic voice recording pipeline.

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
ProcessVoiceData (voice frames from Photon)
    └── FinalizeRecording
            ├── duration guard      →  reject if < ConfigAudioMinDuration (default 0.3s)
            ├── trim trailing silence (analysis only — saved clip stays full)
            ├── VAD.Validate()      →  reject if speechRatio < threshold (sensitivity-dependent)
            └── SaveRecordingAsync
```

The VAD is the **sole content filter** — the previous RMS/Peak pre-filter was removed (the
benchmark showed it was net-negative: it rejected valid speech without improving precision over
accepting everything).

### Files

| File | Role |
|---|---|
| `src/VepFramework/Audio/VadAudioValidator.cs` | VAD wrapper — `VadValidationCriteria`, `VadSensitivity`, `FromSensitivity`, resampling, dispose lock |
| `src/Enemies/Whispral/WhispralMimics.cs` | Recording pipeline — VAD-only, trailing-silence trim, fail-open `try/catch` |
| `src/VepMod.cs` | Config entries (`ConfigVadEnabled`, `ConfigVadSensitivity`, `ConfigAudioMinDuration`) |
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
        MinDurationSeconds = 0f,                    // duration guard is upstream (WhispralMimics)
        MinSpeechRatio     = ratio,
        OperatingMode      = OperatingMode.HighQuality,
        FrameLength        = FrameLength.Is20ms,
        SampleRate         = SampleRate.Is16kHz
    };
}
```

`MinDurationSeconds = 0f` intentionally: `WhispralMimics.FinalizeRecording` reads
`ConfigAudioMinDuration` before calling VAD, keeping the duration threshold user-configurable
without duplicating it inside the VAD criteria.

### Trailing-silence trim

`FinalizeRecording` captures up to `SilenceTimeoutSeconds` (0.5s) of trailing silence. That silence
is trimmed (using `silenceTimer`) from the buffer passed to the VAD so it doesn't dilute the speech
ratio and penalize short utterances. The saved/shared clip keeps the full buffer — only the VAD
analysis window is trimmed. A 0.1s floor avoids degenerate analysis windows.

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

```csharp
if (VepMod.ConfigVadEnabled.Value)
{
    try
    {
        var sensitivity = VepMod.ConfigVadSensitivity.Value;
        var criteria = VadValidationCriteria.FromSensitivity(sensitivity);
        vadValidator = new VadAudioValidator(criteria);
        LOG.Info($"VAD validation enabled (sensitivity={sensitivity}, speechRatio>={criteria.MinSpeechRatio:F2}).");
    }
    catch (Exception ex)
    {
        LOG.Warning($"VAD initialization failed, recordings will not be filtered: {ex.Message}");
        vadValidator = null;
    }
}
```

If `WebRtcVad.dll` (native x64) is absent or fails to load (e.g. on x86 or a broken install),
the constructor throws a `DllNotFoundException`. The catch sets `vadValidator = null` and the
pipeline continues without speech filtering — recordings are accepted as-is, the mod still works.

There are **two** fail-open layers: this one at init, and a second `try/catch` around the runtime
`vadValidator.Validate(...)` call (`ProcessVoiceData` runs on the Photon voice thread, so a native
hiccup must not break voice transmission — on exception the recording is accepted). The native VAD
is also guarded by a lock so `Dispose()` (OnDestroy, Unity thread) cannot free it mid-`Validate()`.

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
WhispralMimics (MonoBehaviour, one per PlayerAvatar)
│
├── Awake()
│     └── StartCoroutine(WaitForVoiceChat)
│           ├── new VadAudioValidator(FromSensitivity(config))   [try/catch]
│           └── StartCoroutine(ShareAudioLoop)      [local player only]
│
├── Loop 1 — ShareAudioLoop (coroutine, local player only)
│     ├── StartRecording()
│     ├── ProcessVoiceData(short[])   ← called by Photon voice pipeline
│     │     └── FinalizeRecording()
│     │           ├── duration < ConfigAudioMinDuration → reject
│     │           ├── trim trailing silence (analysis only)
│     │           ├── vadValidator.Validate() → reject if not enough speech [fail-open]
│     │           └── SaveRecordingAsync() → WAV file + hasNewRecording flag
│     └── ShareAudioWithOthersAsync() → Photon RPC chunks to other players
│
├── RPC ReceiveSharedAudioChunk()     ← all clients
│     └── SaveReceivedAudioAsync() → stored in WavFileManager per player
│
└── Loop 2 — PlayVoiceCommandRPC()    ← sent by Master via EnemyWhispral
      └── PlayAudioAtTransform() → AudioSource on hallucination droid
```
