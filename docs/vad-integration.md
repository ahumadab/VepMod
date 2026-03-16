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
            ├── duration guard  →  reject if < ConfigAudioMinDuration (default 0.3s)
            ├── VAD.Validate()  →  reject if speechRatio < 0.40
            └── SaveRecordingAsync
```

### Files

| File | Role |
|---|---|
| `src/VepFramework/Audio/VadAudioValidator.cs` | VAD wrapper — `VadValidationCriteria`, `VadValidationResult`, resampling |
| `src/VepFramework/Audio/AudioRecordingValidator.cs` | Legacy RMS validator — kept, used by benchmark only |
| `src/VepFramework/Audio/AudioAnalyzer.cs` | Audio stats — kept, used by benchmark only |
| `src/Enemies/Whispral/WhispralMimics.cs` | Recording pipeline — VAD-only + try/catch fallback |
| `src/VepMod.cs` | Config entries — `ConfigAudioMinDuration`, `ConfigVadEnabled` |
| `VepMod.csproj` | Build config — WebRtcVadSharp 1.3.2, PlatformTarget x64, test exclusion |

`src/VepFramework/Audio/CombinedAudioValidator.cs` was **deleted** (dead code — never called).

### `VadValidationCriteria.Production` preset

Defined in `VadAudioValidator.cs:264`:

```csharp
public static VadValidationCriteria Production => new()
{
    MinDurationSeconds = 0f,       // duration guard is upstream (WhispralMimics:325)
    MinSpeechRatio = 0.40f,        // best F1 from benchmark
    OperatingMode = OperatingMode.HighQuality,
    FrameLength = FrameLength.Is20ms,
    SampleRate = SampleRate.Is16kHz
};
```

`MinDurationSeconds = 0f` intentionally: `WhispralMimics.FinalizeRecording` reads
`ConfigAudioMinDuration` before calling VAD, keeping the duration threshold user-configurable
without duplicating it inside the VAD criteria.

### VAD instantiation — try/catch fallback (`WhispralMimics.cs:169`)

```csharp
if (VepMod.ConfigVadEnabled.Value)
{
    try
    {
        vadValidator = new VadAudioValidator(VadValidationCriteria.Production);
    }
    catch (Exception ex)
    {
        LOG.Warning($"VAD initialization failed, recordings will not be filtered: {ex.Message}");
        vadValidator = null;
    }
}
```

If `WebRtcVad.dll` (native x64) is absent or fails to load (e.g. on x86 or a broken install),
the constructor throws. The catch sets `vadValidator = null` and the pipeline continues without
speech filtering — recordings are accepted as-is, the mod still works.

---

## Build configuration (`VepMod.csproj`)

### `PlatformTarget x64`

```xml
<PlatformTarget>x64</PlatformTarget>
```

Required by `WebRtcVad.dll` (native x64 only). Without this, MSBuild emits a warning and
the native DLL may fail to load at runtime on 32-bit hosts.

### `WebRtcVadSharp 1.3.2`

```xml
<PackageReference Include="WebRtcVadSharp" Version="1.3.2"/>
```

Pinned — do not upgrade without re-running the benchmark, since the GMM model behavior
may change across versions.

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

Two entries remain in `[Audio Quality]` in `BepInEx/config/com.vep.vepMod.cfg`:

| Key | Default | Description |
|---|---|---|
| `Min Duration` | `0.3` | Reject recordings shorter than N seconds (pre-VAD guard) |
| `VAD Enabled` | `true` | Enable/disable WebRTC speech detection. Disable if VAD causes issues (e.g. DLL load failure on non-standard setups) |

Removed configs (no longer exist): `AudioMinRms`, `AudioMinPeak`, `AudioMinNonSilenceRatio`.

---

## Final architecture

```
WhispralMimics (MonoBehaviour, one per PlayerAvatar)
│
├── Awake()
│     └── StartCoroutine(WaitForVoiceChat)
│           ├── new VadAudioValidator(Production)   [try/catch]
│           └── StartCoroutine(ShareAudioLoop)      [local player only]
│
├── Loop 1 — ShareAudioLoop (coroutine, local player only)
│     ├── StartRecording()
│     ├── ProcessVoiceData(short[])   ← called by Photon voice pipeline
│     │     └── FinalizeRecording()
│     │           ├── duration < ConfigAudioMinDuration → reject
│     │           ├── vadValidator.Validate() → reject if not enough speech
│     │           └── SaveRecordingAsync() → WAV file + hasNewRecording flag
│     └── ShareAudioWithOthersAsync() → Photon RPC chunks to other players
│
├── RPC ReceiveSharedAudioChunk()     ← all clients
│     └── SaveReceivedAudioAsync() → stored in WavFileManager per player
│
└── Loop 2 — PlayVoiceCommandRPC()    ← sent by Master via EnemyWhispral
      └── PlayAudioAtTransform() → AudioSource on hallucination droid
```
