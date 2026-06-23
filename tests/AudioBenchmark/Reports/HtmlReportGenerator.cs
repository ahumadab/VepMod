using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioBenchmark.Models;

namespace AudioBenchmark.Reports;

/// <summary>
///     Génère un rapport HTML interactif avec lecteur audio et boutons de labellisation.
///     L'utilisateur peut labelliser directement dans le navigateur puis exporter le JSON.
/// </summary>
public static class HtmlReportGenerator
{
    public static void Generate(string outputPath, DatasetLabels labels, string datasetBasePath)
    {
        var unknowns = labels.Labels.Where(l => l.Label == AudioLabel.Unknown).ToList();
        var autoSpeech = labels.Labels.Where(l => l.Label == AudioLabel.Speech && l.Source == LabelSource.Auto).ToList();
        var autoNoise = labels.Labels.Where(l => l.Label == AudioLabel.Noise && l.Source == LabelSource.Auto).ToList();

        // Sérialiser les labels comme JSON embarqué dans le HTML
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };
        var labelsJson = JsonSerializer.Serialize(labels, jsonOptions);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='fr'><head><meta charset='utf-8'>");
        sb.AppendLine("<title>VepMod - Labellisation Audio</title>");
        WriteStyles(sb);
        sb.AppendLine("</head><body>");

        // Header
        sb.AppendLine("<h1>VepMod - Labellisation Audio Interactive</h1>");

        // Barre de progression sticky en haut
        sb.AppendLine("<div id='progress-bar'>");
        sb.AppendLine("  <div class='progress-inner'>");
        sb.AppendLine("    <span id='progress-text'>Chargement...</span>");
        sb.AppendLine("    <div class='progress-track'><div id='progress-fill' class='progress-fill'></div></div>");
        sb.AppendLine("    <div class='progress-counts'>");
        sb.AppendLine("      <span class='count-speech' id='count-speech'>0</span> Parole");
        sb.AppendLine("      <span class='count-noise' id='count-noise'>0</span> Bruit");
        sb.AppendLine("      <span class='count-unknown' id='count-unknown'>0</span> Restant");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </div>");
        sb.AppendLine("  <div class='progress-actions'>");
        sb.AppendLine("    <button onclick='exportLabels()' class='btn-export'>Exporter labels.json</button>");
        sb.AppendLine("    <button onclick='toggleAutoScroll()' id='btn-autoscroll' class='btn-secondary'>Auto-scroll: ON</button>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");

        // Instructions
        sb.AppendLine("<div class='instructions'>");
        sb.AppendLine("<h3>Mode d'emploi</h3>");
        sb.AppendLine("<ul>");
        sb.AppendLine("<li>Clique sur le player audio pour ecouter, puis clique <b>Parole</b> ou <b>Bruit</b>.</li>");
        sb.AppendLine("<li>Raccourcis clavier : <kbd>S</kbd> = Parole (Speech), <kbd>N</kbd> = Bruit (Noise), <kbd>U</kbd> = Reset (Unknown), <kbd>Space</kbd> = Play/Pause.</li>");
        sb.AppendLine("<li>La ligne en surbrillance bleue est la ligne active (clique sur une ligne pour la selectionner).</li>");
        sb.AppendLine("<li>Apres la labellisation, clique <b>Exporter labels.json</b> et sauvegarde le fichier.</li>");
        sb.AppendLine("<li>Relance le benchmark : <code>dotnet run -- [dataset] labels.json</code></li>");
        sb.AppendLine("</ul></div>");

        // Section 1: Fichiers A VERIFIER
        sb.AppendLine("<h2 id='section-unknown'>A verifier manuellement</h2>");
        sb.AppendLine($"<p>{unknowns.Count} fichiers ambigus. Tries par speech ratio croissant.</p>");
        WriteInteractiveTable(sb, unknowns, datasetBasePath, "unknown");

        // Section 2: Auto BRUIT
        sb.AppendLine("<h2 id='section-noise'>Auto-classifie : BRUIT</h2>");
        sb.AppendLine($"<p>{autoNoise.Count} fichiers. Tu peux corriger si l'auto-classification s'est trompee.</p>");
        WriteInteractiveTable(sb, autoNoise, datasetBasePath, "noise");

        // Section 3: Auto PAROLE
        sb.AppendLine("<h2 id='section-speech'>Auto-classifie : PAROLE</h2>");
        sb.AppendLine($"<p>{autoSpeech.Count} fichiers. Tu peux corriger si l'auto-classification s'est trompee.</p>");
        WriteInteractiveTable(sb, autoSpeech, datasetBasePath, "speech");

        // Embed les données JSON + le script interactif
        sb.AppendLine($"<script>const labelsData = {labelsJson};</script>");
        WriteScript(sb);

        sb.AppendLine("</body></html>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteStyles(StringBuilder sb)
    {
        sb.AppendLine("<style>");
        sb.AppendLine(@"
* { box-sizing: border-box; }
body { font-family: 'Segoe UI', sans-serif; max-width: 1600px; margin: 0 auto; padding: 20px; padding-top: 100px; background: #1a1a2e; color: #eee; }
h1 { color: #e94560; margin-bottom: 4px; }
h2 { color: #0f3460; background: #e94560; padding: 8px 16px; border-radius: 4px; margin-top: 30px; }
h3 { color: #e94560; border-bottom: 1px solid #333; padding-bottom: 4px; margin-bottom: 8px; }

/* Progress bar sticky */
#progress-bar {
  position: fixed; top: 0; left: 0; right: 0; z-index: 1000;
  background: #0f0f23; border-bottom: 2px solid #e94560;
  padding: 8px 20px; display: flex; align-items: center; gap: 20px;
}
.progress-inner { flex: 1; display: flex; align-items: center; gap: 16px; }
#progress-text { font-weight: bold; min-width: 120px; }
.progress-track { flex: 1; height: 14px; background: #16213e; border-radius: 7px; overflow: hidden; }
.progress-fill { height: 100%; background: linear-gradient(90deg, #4caf50, #8bc34a); border-radius: 7px; transition: width 0.3s; width: 0%; }
.progress-counts { display: flex; gap: 12px; font-size: 0.85em; min-width: 280px; }
.count-speech { color: #4caf50; font-weight: bold; }
.count-noise { color: #f44336; font-weight: bold; }
.count-unknown { color: #ff9800; font-weight: bold; }
.progress-actions { display: flex; gap: 8px; }

/* Buttons */
.btn-export { background: #4caf50; color: white; border: none; padding: 8px 16px; border-radius: 4px; cursor: pointer; font-weight: bold; white-space: nowrap; }
.btn-export:hover { background: #388e3c; }
.btn-secondary { background: #16213e; color: #aaa; border: 1px solid #333; padding: 8px 12px; border-radius: 4px; cursor: pointer; white-space: nowrap; }
.btn-secondary:hover { background: #1a2744; }
.btn-speech { background: #4caf50; color: white; border: none; padding: 4px 12px; border-radius: 3px; cursor: pointer; font-size: 0.8em; font-weight: bold; }
.btn-speech:hover { background: #388e3c; }
.btn-noise { background: #f44336; color: white; border: none; padding: 4px 12px; border-radius: 3px; cursor: pointer; font-size: 0.8em; font-weight: bold; }
.btn-noise:hover { background: #c62828; }
.btn-reset { background: #555; color: #ccc; border: none; padding: 4px 8px; border-radius: 3px; cursor: pointer; font-size: 0.75em; }
.btn-reset:hover { background: #777; }

/* Table */
table { border-collapse: collapse; width: 100%; margin: 10px 0; }
th, td { padding: 5px 8px; border: 1px solid #333; text-align: left; font-size: 0.82em; }
th { background: #16213e; color: #e94560; position: sticky; top: 80px; z-index: 10; }
tr:nth-child(even) { background: #16213e20; }
tr:hover { background: #16213e60; }
tr.active { background: #1a3a6e !important; outline: 2px solid #4fc3f7; }
tr.labeled-speech { border-left: 4px solid #4caf50; }
tr.labeled-noise { border-left: 4px solid #f44336; }

/* Labels dans les cellules */
.label-display { font-weight: bold; min-width: 60px; display: inline-block; text-align: center; padding: 2px 6px; border-radius: 3px; }
.label-speech { color: #4caf50; background: #4caf5020; }
.label-noise { color: #f44336; background: #f4433620; }
.label-unknown { color: #ff9800; background: #ff980020; }

.bar { display: inline-block; height: 12px; border-radius: 2px; vertical-align: middle; }
.instructions { background: #16213e; border: 2px solid #e94560; border-radius: 8px; padding: 12px 16px; margin: 16px 0; }
.instructions ul { margin: 4px 0; padding-left: 20px; }
.instructions li { margin: 4px 0; }
kbd { background: #0f3460; padding: 2px 8px; border-radius: 3px; border: 1px solid #555; font-family: monospace; font-size: 0.9em; }
audio { height: 30px; vertical-align: middle; }
code { background: #0f3460; padding: 2px 6px; border-radius: 3px; }
");
        sb.AppendLine("</style>");
    }

    private static void WriteInteractiveTable(StringBuilder sb, List<FileLabel> labels, string basePath, string section)
    {
        if (labels.Count == 0)
        {
            sb.AppendLine("<p><i>Aucun fichier dans cette categorie.</i></p>");
            return;
        }

        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>#</th><th>Joueur</th><th>Fichier</th><th>Duree</th><th>RMS</th><th>Peak</th><th>Speech%</th><th>Bar</th><th>Label</th><th>Actions</th><th>Audio</th></tr>");

        var sorted = labels.OrderBy(l => l.SpeechRatio).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var l = sorted[i];
            var playerFolder = $"player_{l.PlayerName}";
            var audioPath = Path.Combine(basePath, playerFolder, l.FileName).Replace('\\', '/');

            var barWidth = (int)(l.SpeechRatio * 150);
            var barColor = l.SpeechRatio > 0.5 ? "#4caf50" : l.SpeechRatio > 0.2 ? "#ff9800" : "#f44336";

            var labelClass = l.Label switch
            {
                AudioLabel.Speech => "label-speech",
                AudioLabel.Noise => "label-noise",
                _ => "label-unknown"
            };

            var rowClass = l.Label switch
            {
                AudioLabel.Speech => "labeled-speech",
                AudioLabel.Noise => "labeled-noise",
                _ => ""
            };

            // data-filename est l'identifiant unique pour retrouver le label dans labelsData
            sb.AppendLine($"<tr class='{rowClass}' data-filename='{EscapeHtml(l.FileName)}' onclick='selectRow(this)'>");
            sb.AppendLine($"<td>{i + 1}</td>");
            sb.AppendLine($"<td>{EscapeHtml(l.PlayerName)}</td>");
            sb.AppendLine($"<td style='font-size:0.75em'>{EscapeHtml(l.FileName)}</td>");
            sb.AppendLine($"<td>{l.DurationSeconds:F2}s</td>");
            sb.AppendLine($"<td>{l.Rms:F4}</td>");
            sb.AppendLine($"<td>{l.PeakAmplitude:F4}</td>");
            sb.AppendLine($"<td>{l.SpeechRatio:P0}</td>");
            sb.AppendLine($"<td><span class='bar' style='width:{barWidth}px;background:{barColor}'></span></td>");
            sb.AppendLine($"<td><span class='label-display {labelClass}' data-label-display>{l.Label}</span></td>");
            sb.AppendLine($"<td style='white-space:nowrap'>");
            sb.AppendLine($"  <button class='btn-speech' onclick=\"setLabel(this, 'Speech'); event.stopPropagation();\">Parole</button>");
            sb.AppendLine($"  <button class='btn-noise' onclick=\"setLabel(this, 'Noise'); event.stopPropagation();\">Bruit</button>");
            sb.AppendLine($"  <button class='btn-reset' onclick=\"setLabel(this, 'Unknown'); event.stopPropagation();\">Reset</button>");
            sb.AppendLine($"</td>");
            sb.AppendLine($"<td><audio controls preload='none' src='file:///{audioPath}'></audio></td>");
            sb.AppendLine($"</tr>");
        }
        sb.AppendLine("</table>");
    }

    private static void WriteScript(StringBuilder sb)
    {
        sb.AppendLine(@"<script>
let activeRow = null;
let autoScroll = true;

// --- Initialisation ---
window.addEventListener('DOMContentLoaded', () => {
    updateProgress();
    // Sélectionner la première ligne Unknown
    const firstUnknown = document.querySelector('tr[data-filename] .label-unknown');
    if (firstUnknown) selectRow(firstUnknown.closest('tr'));
});

// --- Sélection de ligne ---
function selectRow(tr) {
    if (activeRow) activeRow.classList.remove('active');
    activeRow = tr;
    tr.classList.add('active');
}

// --- Labellisation ---
function setLabel(btn, label) {
    const tr = btn.closest('tr');
    const filename = tr.dataset.filename;

    // Mettre à jour labelsData
    const entry = labelsData.Labels.find(l => l.FileName === filename);
    if (entry) {
        entry.Label = label;
        entry.Source = 'Manual';
        entry.Reason = 'Labellise manuellement via HTML';
    }

    // Mettre à jour l'affichage
    const display = tr.querySelector('[data-label-display]');
    display.textContent = label;
    display.className = 'label-display label-' + label.toLowerCase();

    tr.classList.remove('labeled-speech', 'labeled-noise');
    if (label === 'Speech') tr.classList.add('labeled-speech');
    else if (label === 'Noise') tr.classList.add('labeled-noise');

    updateProgress();

    // Auto-avancer vers la prochaine ligne non labellisée
    if (autoScroll && label !== 'Unknown') {
        advanceToNext(tr);
    }
}

function advanceToNext(currentTr) {
    let next = currentTr.nextElementSibling;
    while (next) {
        const display = next.querySelector('[data-label-display]');
        if (display && display.textContent === 'Unknown') {
            selectRow(next);
            next.scrollIntoView({ behavior: 'smooth', block: 'center' });
            // Lancer l'audio automatiquement
            const audio = next.querySelector('audio');
            if (audio) { audio.load(); audio.play().catch(() => {}); }
            return;
        }
        next = next.nextElementSibling;
    }
    // Si plus rien dans cette table, chercher dans les tables suivantes
    const tables = document.querySelectorAll('table');
    let foundCurrent = false;
    for (const table of tables) {
        if (table.contains(currentTr)) { foundCurrent = true; continue; }
        if (!foundCurrent) continue;
        const rows = table.querySelectorAll('tr[data-filename]');
        for (const row of rows) {
            const display = row.querySelector('[data-label-display]');
            if (display && display.textContent === 'Unknown') {
                selectRow(row);
                row.scrollIntoView({ behavior: 'smooth', block: 'center' });
                const audio = row.querySelector('audio');
                if (audio) { audio.load(); audio.play().catch(() => {}); }
                return;
            }
        }
    }
}

// --- Progression ---
function updateProgress() {
    const total = labelsData.Labels.length;
    const speech = labelsData.Labels.filter(l => l.Label === 'Speech').length;
    const noise = labelsData.Labels.filter(l => l.Label === 'Noise').length;
    const unknown = labelsData.Labels.filter(l => l.Label === 'Unknown').length;
    const labeled = speech + noise;
    const pct = total > 0 ? (labeled / total * 100) : 0;

    document.getElementById('progress-fill').style.width = pct + '%';
    document.getElementById('progress-text').textContent = labeled + '/' + total + ' (' + pct.toFixed(0) + '%)';
    document.getElementById('count-speech').textContent = speech;
    document.getElementById('count-noise').textContent = noise;
    document.getElementById('count-unknown').textContent = unknown;
}

// --- Raccourcis clavier ---
document.addEventListener('keydown', (e) => {
    // Ignorer si on tape dans un input
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;

    if (!activeRow) return;

    if (e.key === 's' || e.key === 'S') {
        e.preventDefault();
        setLabel(activeRow.querySelector('.btn-speech'), 'Speech');
    } else if (e.key === 'n' || e.key === 'N') {
        e.preventDefault();
        setLabel(activeRow.querySelector('.btn-noise'), 'Noise');
    } else if (e.key === 'u' || e.key === 'U') {
        e.preventDefault();
        setLabel(activeRow.querySelector('.btn-reset'), 'Unknown');
    } else if (e.key === ' ') {
        e.preventDefault();
        const audio = activeRow.querySelector('audio');
        if (audio) {
            if (audio.paused) { audio.load(); audio.play().catch(() => {}); }
            else audio.pause();
        }
    } else if (e.key === 'ArrowDown') {
        e.preventDefault();
        const next = activeRow.nextElementSibling;
        if (next && next.dataset.filename) {
            selectRow(next);
            next.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        const prev = activeRow.previousElementSibling;
        if (prev && prev.dataset.filename) {
            selectRow(prev);
            prev.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }
});

// --- Toggle auto-scroll ---
function toggleAutoScroll() {
    autoScroll = !autoScroll;
    document.getElementById('btn-autoscroll').textContent = 'Auto-scroll: ' + (autoScroll ? 'ON' : 'OFF');
}

// --- Export JSON ---
function exportLabels() {
    // Mettre à jour les compteurs dans le JSON
    labelsData.AutoLabeled = labelsData.Labels.filter(l => l.Source === 'Auto' && l.Label !== 'Unknown').length;
    labelsData.ManualRequired = labelsData.Labels.filter(l => l.Label === 'Unknown').length;

    const json = JSON.stringify(labelsData, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'labels.json';
    a.click();
    URL.revokeObjectURL(url);
}
</script>");
    }

    private static string EscapeHtml(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&#39;").Replace("\"", "&quot;");
    }
}
