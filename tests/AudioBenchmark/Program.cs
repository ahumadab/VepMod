using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioBenchmark.Models;
using AudioBenchmark.Reports;
using AudioBenchmark.Validators;

const string DEFAULT_DATASET_PATH = @"C:\Program Files (x86)\Steam\steamapps\common\REPO\REPO_Data\AudioFiles";

// --- Seuils heuristiques pour auto-classification ---
// Un fichier est AUTO-NOISE si TOUTES ces conditions sont vraies:
const float AUTO_NOISE_MAX_RMS = 0.004f;        // RMS très faible -> quasi-silence
const float AUTO_NOISE_MAX_PEAK = 0.02f;         // Aucun pic significatif
const float AUTO_NOISE_MAX_SPEECH = 0.05f;       // VAD ne détecte quasi rien

// Un fichier est AUTO-SPEECH si TOUTES ces conditions sont vraies:
const float AUTO_SPEECH_MIN_RMS = 0.025f;        // RMS clairement au-dessus du bruit
const float AUTO_SPEECH_MIN_PEAK = 0.15f;        // Pics clairs
const float AUTO_SPEECH_MIN_SPEECH = 0.50f;      // VAD détecte >50% de parole

var datasetPath = args.Length > 0 ? args[0] : DEFAULT_DATASET_PATH;
var labelsPath = args.Length > 1 ? args[1] : null;
var outputDir = Path.Combine(AppContext.BaseDirectory, "results");
Directory.CreateDirectory(outputDir);

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("=======================================================");
Console.WriteLine("  VepMod Audio Pipeline Benchmark v2");
Console.WriteLine("  Avec classification ground truth");
Console.WriteLine("=======================================================");
Console.WriteLine($"\n  Dataset: {datasetPath}");

if (!Directory.Exists(datasetPath))
{
    Console.WriteLine($"\n  ERREUR: Le dossier '{datasetPath}' n'existe pas.");
    return 1;
}

// ===== Phase 1: Charger le dataset =====
Console.WriteLine("\n--- Phase 1: Chargement du dataset ---");
var sw = Stopwatch.StartNew();
var dataset = WavReader.LoadDataset(datasetPath);
sw.Stop();
Console.WriteLine($"  Chargé: {dataset.Count} fichiers WAV en {sw.ElapsedMilliseconds}ms");

if (dataset.Count == 0)
{
    Console.WriteLine("  ERREUR: Aucun fichier WAV trouvé.");
    return 1;
}

// Stats du dataset
var players = dataset.GroupBy(w => w.PlayerName).OrderBy(g => g.Key).ToList();
Console.WriteLine($"  Joueurs: {players.Count}");
Console.WriteLine($"  Durée totale: {dataset.Sum(w => w.DurationSeconds):F1}s");
Console.WriteLine($"  Durée moyenne: {dataset.Average(w => w.DurationSeconds):F2}s");
Console.WriteLine($"  Sample rates: {string.Join(", ", dataset.Select(w => w.SampleRate).Distinct().OrderBy(x => x).Select(x => $"{x}Hz"))}");

// ===== Phase 2: Analyse audio + VAD =====
Console.WriteLine("\n--- Phase 2: Analyse audio ---");

using var vadDefault = new VadAudioValidator(VadValidationCriteria.Default);
using var vadStrict = new VadAudioValidator(VadValidationCriteria.Strict);

// Collecter les métriques brutes de chaque fichier
var fileMetrics = new List<(WavFile wav, AudioAnalysisResult analysis, VadAnalysisResult vadDefault, VadAnalysisResult vadStrict)>();

foreach (var wav in dataset)
{
    var analysis = AudioAnalyzer.Analyze(wav.Samples, wav.SampleRate);
    var vd = vadDefault.Validate(wav.Samples, wav.SampleRate);
    var vs = vadStrict.Validate(wav.Samples, wav.SampleRate);
    var vadDefAnalysis = vd.Analysis ?? new VadAnalysisResult(0, 0, 0, 0);
    var vadStrAnalysis = vs.Analysis ?? new VadAnalysisResult(0, 0, 0, 0);
    fileMetrics.Add((wav, analysis, vadDefAnalysis, vadStrAnalysis));
}

// ===== Phase 3: Auto-classification heuristique =====
Console.WriteLine("\n--- Phase 3: Auto-classification ---");

DatasetLabels labels;
var labelsOutputPath = Path.Combine(outputDir, "labels.json");

if (labelsPath != null && File.Exists(labelsPath))
{
    // Charger les labels existants (potentiellement complétés manuellement)
    labels = DatasetLabels.Load(labelsPath);
    Console.WriteLine($"  Labels chargés depuis: {labelsPath}");
    Console.WriteLine($"  - Speech: {labels.Labels.Count(l => l.Label == AudioLabel.Speech)}");
    Console.WriteLine($"  - Noise:  {labels.Labels.Count(l => l.Label == AudioLabel.Noise)}");
    Console.WriteLine($"  - Unknown: {labels.Labels.Count(l => l.Label == AudioLabel.Unknown)}");
}
else
{
    // Générer les labels automatiques
    labels = new DatasetLabels
    {
        DatasetPath = datasetPath,
        TotalFiles = dataset.Count
    };

    foreach (var (wav, analysis, vadDef, vadStr) in fileMetrics)
    {
        AudioLabel label;
        LabelSource source;
        string reason;

        if (analysis.Rms <= AUTO_NOISE_MAX_RMS && analysis.PeakAmplitude <= AUTO_NOISE_MAX_PEAK && vadDef.SpeechRatio <= AUTO_NOISE_MAX_SPEECH)
        {
            label = AudioLabel.Noise;
            source = LabelSource.Auto;
            reason = $"RMS={analysis.Rms:F4} Peak={analysis.PeakAmplitude:F4} Speech={vadDef.SpeechRatio:P0} -> silence/bruit evident";
        }
        else if (analysis.Rms >= AUTO_SPEECH_MIN_RMS && analysis.PeakAmplitude >= AUTO_SPEECH_MIN_PEAK && vadDef.SpeechRatio >= AUTO_SPEECH_MIN_SPEECH)
        {
            label = AudioLabel.Speech;
            source = LabelSource.Auto;
            reason = $"RMS={analysis.Rms:F4} Peak={analysis.PeakAmplitude:F4} Speech={vadDef.SpeechRatio:P0} -> parole evidente";
        }
        else
        {
            label = AudioLabel.Unknown;
            source = LabelSource.Auto;
            reason = $"Ambigu: RMS={analysis.Rms:F4} Peak={analysis.PeakAmplitude:F4} Speech={vadDef.SpeechRatio:P0}";
        }

        labels.Labels.Add(new FileLabel
        {
            FileName = wav.FileName,
            PlayerName = wav.PlayerName,
            Label = label,
            Source = source,
            Reason = reason,
            Rms = analysis.Rms,
            PeakAmplitude = analysis.PeakAmplitude,
            SpeechRatio = vadDef.SpeechRatio,
            DurationSeconds = wav.DurationSeconds
        });
    }

    labels.AutoLabeled = labels.Labels.Count(l => l.Label != AudioLabel.Unknown);
    labels.ManualRequired = labels.Labels.Count(l => l.Label == AudioLabel.Unknown);

    labels.Save(labelsOutputPath);
    Console.WriteLine($"  Labels sauvegardés: {labelsOutputPath}");
}

var autoSpeechCount = labels.Labels.Count(l => l.Label == AudioLabel.Speech && l.Source == LabelSource.Auto);
var autoNoiseCount = labels.Labels.Count(l => l.Label == AudioLabel.Noise && l.Source == LabelSource.Auto);
var manualSpeechCount = labels.Labels.Count(l => l.Label == AudioLabel.Speech && l.Source == LabelSource.Manual);
var manualNoiseCount = labels.Labels.Count(l => l.Label == AudioLabel.Noise && l.Source == LabelSource.Manual);
var unknownCount = labels.Labels.Count(l => l.Label == AudioLabel.Unknown);
var totalSpeech = labels.Labels.Count(l => l.Label == AudioLabel.Speech);
var totalNoise = labels.Labels.Count(l => l.Label == AudioLabel.Noise);

Console.WriteLine($"\n  Classification du dataset:");
Console.WriteLine($"    PAROLE: {totalSpeech} ({autoSpeechCount} auto + {manualSpeechCount} manuel)");
Console.WriteLine($"    BRUIT:  {totalNoise} ({autoNoiseCount} auto + {manualNoiseCount} manuel)");
Console.WriteLine($"    A VERIFIER: {unknownCount}");
Console.WriteLine($"    Total labellisé: {totalSpeech + totalNoise}/{labels.TotalFiles}");

// ===== Phase 4: Évaluation du pipeline avec les labels =====
Console.WriteLine("\n--- Phase 4: Évaluation du pipeline ---");

if (unknownCount > 0)
{
    Console.WriteLine($"\n  ATTENTION: {unknownCount} fichiers non labellisés (Unknown).");
    Console.WriteLine("  Les métriques ci-dessous sont calculées uniquement sur les fichiers labellisés.");
    Console.WriteLine($"  Pour un résultat complet, éditez {labelsOutputPath}");
    Console.WriteLine("  et re-exécutez: dotnet run -- [dataset_path] [labels.json]");
}

// Construire un lookup des labels par nom de fichier
var labelLookup = labels.Labels.ToDictionary(l => l.FileName, l => l.Label);

// Créer les validateurs à évaluer
var preFilterGame = new AudioRecordingValidator(AudioValidationCriteria.Game);
var preFilterDefault = new AudioRecordingValidator(AudioValidationCriteria.Default);
var preFilterStrict = new AudioRecordingValidator(AudioValidationCriteria.Strict);
var preFilterPermissive = new AudioRecordingValidator(AudioValidationCriteria.Permissive);

// Évaluer chaque configuration du pipeline
var configs = new List<(string Name, Func<WavFile, bool> Predicate)>
{
    ("PreFilter Game", wav => preFilterGame.Validate(wav.Samples, wav.SampleRate).IsValid),
    ("PreFilter Default", wav => preFilterDefault.Validate(wav.Samples, wav.SampleRate).IsValid),
    ("PreFilter Strict", wav => preFilterStrict.Validate(wav.Samples, wav.SampleRate).IsValid),
    ("PreFilter Permissive", wav => preFilterPermissive.Validate(wav.Samples, wav.SampleRate).IsValid),
};

// Ajouter les configs VAD avec différents seuils de speech ratio
foreach (var minSpeech in new[] { 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.40f, 0.50f, 0.60f })
{
    var threshold = minSpeech;
    configs.Add(($"VAD speechRatio>={threshold:F2}", wav =>
    {
        var metrics = fileMetrics.First(m => m.wav.FileName == wav.FileName);
        return metrics.vadDefault.SpeechRatio >= threshold;
    }));
}

// Ajouter le pipeline combiné actuel (PreFilter permissif + VAD Strict)
configs.Add(("Combined (actuel)", wav =>
{
    var pf = new AudioRecordingValidator(new AudioValidationCriteria
    {
        MinDurationSeconds = 0.3f, MinRms = 0.003f, MinPeakAmplitude = 0.01f,
        MinNonSilenceRatio = 0.01f, SilenceThreshold = 0.002f
    });
    if (!pf.Validate(wav.Samples, wav.SampleRate).IsValid) return false;
    var metrics = fileMetrics.First(m => m.wav.FileName == wav.FileName);
    return metrics.vadStrict.SpeechRatio >= 0.60f;
}));

// Ajouter le pipeline Game PreFilter + VAD Default
configs.Add(("Game + VAD>=0.10", wav =>
{
    if (!preFilterGame.Validate(wav.Samples, wav.SampleRate).IsValid) return false;
    var metrics = fileMetrics.First(m => m.wav.FileName == wav.FileName);
    return metrics.vadDefault.SpeechRatio >= 0.10f;
}));

// Ajouter Game PreFilter + VAD avec seuil intermédiaire
configs.Add(("Game + VAD>=0.25", wav =>
{
    if (!preFilterGame.Validate(wav.Samples, wav.SampleRate).IsValid) return false;
    var metrics = fileMetrics.First(m => m.wav.FileName == wav.FileName);
    return metrics.vadDefault.SpeechRatio >= 0.25f;
}));

// Filtrer uniquement les fichiers labellisés (Speech ou Noise)
var labeledFiles = dataset.Where(w => labelLookup.TryGetValue(w.FileName, out var l) && l != AudioLabel.Unknown).ToList();

if (labeledFiles.Count >= 10)
{
    Console.WriteLine($"\n  Évaluation sur {labeledFiles.Count} fichiers labellisés ({totalSpeech} speech, {totalNoise} noise):");
    Console.WriteLine();
    Console.WriteLine($"  {"Config",-25} {"TP",4} {"FP",4} {"FN",4} {"TN",4} {"Prec",7} {"Recall",7} {"F1",7} {"Acc",7}");
    Console.WriteLine($"  {new string('-', 82)}");

    foreach (var (name, predicate) in configs)
    {
        var tp = 0; var fp = 0; var fn = 0; var tn = 0;

        foreach (var wav in labeledFiles)
        {
            var actual = labelLookup[wav.FileName];
            var predicted = predicate(wav);

            // predicted=true (Accept) -> on dit "c'est de la parole"
            // actual=Speech -> c'est vraiment de la parole
            if (predicted && actual == AudioLabel.Speech) tp++;
            else if (predicted && actual == AudioLabel.Noise) fp++;
            else if (!predicted && actual == AudioLabel.Speech) fn++;
            else if (!predicted && actual == AudioLabel.Noise) tn++;
        }

        var precision = tp + fp > 0 ? (float)tp / (tp + fp) : 0f;
        var recall = tp + fn > 0 ? (float)tp / (tp + fn) : 0f;
        var f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0f;
        var accuracy = labeledFiles.Count > 0 ? (float)(tp + tn) / labeledFiles.Count : 0f;

        Console.WriteLine($"  {name,-25} {tp,4} {fp,4} {fn,4} {tn,4} {precision,6:P0} {recall,6:P0} {f1,6:P0} {accuracy,6:P0}");
    }

    Console.WriteLine();
    Console.WriteLine("  Légende:");
    Console.WriteLine("    TP = Vraie parole correctement acceptée (bon)");
    Console.WriteLine("    FP = Bruit incorrectement accepté (mauvais -> hallucination de bruit)");
    Console.WriteLine("    FN = Vraie parole rejetée (mauvais -> on perd de la parole)");
    Console.WriteLine("    TN = Bruit correctement rejeté (bon)");
    Console.WriteLine("    Precision = TP/(TP+FP) : parmi les acceptés, combien sont de la vraie parole ?");
    Console.WriteLine("    Recall = TP/(TP+FN) : parmi la vraie parole, combien est acceptée ?");
    Console.WriteLine("    F1 = moyenne harmonique de Precision et Recall");
}
else
{
    Console.WriteLine("\n  Pas assez de fichiers labellisés pour calculer precision/recall.");
    Console.WriteLine("  Labellisez au moins 10 fichiers dans labels.json et re-exécutez.");
}

// ===== Phase 5: Performance =====
Console.WriteLine("\n--- Phase 5: Performance ---");
var perfSw = Stopwatch.StartNew();
foreach (var wav in dataset)
{
    preFilterGame.Validate(wav.Samples, wav.SampleRate);
}
perfSw.Stop();
var preFilterAvgMs = perfSw.Elapsed.TotalMilliseconds / dataset.Count;

perfSw.Restart();
using (var vadPerf = new VadAudioValidator(VadValidationCriteria.Default))
{
    foreach (var wav in dataset)
        vadPerf.Validate(wav.Samples, wav.SampleRate);
}
perfSw.Stop();
var vadAvgMs = perfSw.Elapsed.TotalMilliseconds / dataset.Count;

Console.WriteLine($"  PreFilter Game moyen: {preFilterAvgMs:F3}ms/fichier");
Console.WriteLine($"  VAD Default moyen:    {vadAvgMs:F3}ms/fichier");
Console.WriteLine($"  Total combiné:        {preFilterAvgMs + vadAvgMs:F3}ms/fichier");
Console.WriteLine($"  {(vadAvgMs < 50 ? "-> Compatible runtime Unity (< 50ms)" : "-> ATTENTION: pourrait causer des freezes")}");

// ===== Export CSV et HTML =====
Console.WriteLine("\n--- Exports ---");

// CSV brut avec toutes les métriques
var csvPath = Path.Combine(outputDir, "benchmark_results.csv");
var csvLines = new List<string>
{
    "FileName,PlayerName,DurationSec,SampleRate,RMS,Peak,NonSilenceRatio,SpeechRatio_Default,SpeechRatio_Strict,Label,LabelSource,PreFilter_Game,PreFilter_Default,VAD_Default,VAD_Strict"
};

foreach (var (wav, analysis, vadDef, vadStr) in fileMetrics)
{
    var lbl = labels.Labels.FirstOrDefault(l => l.FileName == wav.FileName);
    var pfGame = preFilterGame.Validate(wav.Samples, wav.SampleRate);
    var pfDefault = preFilterDefault.Validate(wav.Samples, wav.SampleRate);

    csvLines.Add(string.Join(",",
        wav.FileName,
        wav.PlayerName,
        wav.DurationSeconds.ToString("F3"),
        wav.SampleRate,
        analysis.Rms.ToString("F6"),
        analysis.PeakAmplitude.ToString("F6"),
        analysis.NonSilenceRatio.ToString("F4"),
        vadDef.SpeechRatio.ToString("F4"),
        vadStr.SpeechRatio.ToString("F4"),
        lbl?.Label.ToString() ?? "Unknown",
        lbl?.Source.ToString() ?? "Auto",
        pfGame.IsValid ? "Accept" : pfGame.RejectionReason.ToString(),
        pfDefault.IsValid ? "Accept" : pfDefault.RejectionReason.ToString(),
        vadDef.SpeechRatio >= 0.10f ? "Accept" : "Reject",
        vadStr.SpeechRatio >= 0.60f ? "Accept" : "Reject"
    ));
}
File.WriteAllLines(csvPath, csvLines);
Console.WriteLine($"  CSV: {csvPath}");

// HTML pour écoute manuelle
var htmlPath = Path.Combine(outputDir, "labeling_report.html");
HtmlReportGenerator.Generate(htmlPath, labels, datasetPath);
Console.WriteLine($"  HTML: {htmlPath}");

// ===== Conclusion =====
Console.WriteLine("\n=======================================================");
Console.WriteLine("  RÉSUMÉ");
Console.WriteLine("=======================================================");
Console.WriteLine($"\n  Dataset: {dataset.Count} fichiers, {players.Count} joueurs");
Console.WriteLine($"  Classifiés: {totalSpeech} parole, {totalNoise} bruit, {unknownCount} à vérifier");
Console.WriteLine($"  Performance VAD: {vadAvgMs:F2}ms/fichier (compatible Unity)");

if (labeledFiles.Count >= 10)
{
    // Trouver la meilleure config
    var bestF1 = 0f;
    var bestName = "";
    foreach (var (name, predicate) in configs)
    {
        var tp = 0; var fp = 0; var fn = 0;
        foreach (var wav2 in labeledFiles)
        {
            var actual = labelLookup[wav2.FileName];
            var predicted = predicate(wav2);
            if (predicted && actual == AudioLabel.Speech) tp++;
            else if (predicted && actual == AudioLabel.Noise) fp++;
            else if (!predicted && actual == AudioLabel.Speech) fn++;
        }
        var precision = tp + fp > 0 ? (float)tp / (tp + fp) : 0f;
        var recall = tp + fn > 0 ? (float)tp / (tp + fn) : 0f;
        var f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0f;
        if (f1 > bestF1) { bestF1 = f1; bestName = name; }
    }
    Console.WriteLine($"\n  Meilleure config (F1): {bestName} (F1={bestF1:P0})");
}

if (unknownCount > 0)
{
    Console.WriteLine($"\n  PROCHAINE ETAPE:");
    Console.WriteLine($"  1. Ouvrez le rapport HTML: {htmlPath}");
    Console.WriteLine($"  2. Écoutez les {unknownCount} fichiers ambigus");
    Console.WriteLine($"  3. Éditez labels.json: changez \"Unknown\" -> \"Speech\" ou \"Noise\"");
    Console.WriteLine($"  4. Re-exécutez: dotnet run -- \"{datasetPath}\" \"{labelsOutputPath}\"");
}

Console.WriteLine("\n=======================================================\n");
return 0;
