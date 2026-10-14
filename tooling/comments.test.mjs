import assert from "node:assert/strict";
import { test } from "node:test";
import { avoidedWords, commentLinesMatching, french, stringLinesMatching } from "./comments.mjs";

const frenchCommentLines = (file, text) => commentLinesMatching(french, file, text);

test("flags French in C# line and block comments", () => {
    const code = ["// Vérifie la valeur", "/* Déjà", " * à revoir", " */", "var x = 1; // reçu"].join("\n");
    assert.deepEqual(frenchCommentLines("A.cs", code), [1, 2, 3, 5]);
});

test("flags French in PowerShell comments", () => {
    const code = ["# Vérifie la DLL", "$x = 1 # reçu", "Write-Host 'ok'"].join("\n");
    assert.deepEqual(frenchCommentLines("build.ps1", code), [1, 2]);
});

test("flags French without accents", () => {
    const code = ["// Trouver le joueur cible", "// Contrôleur", "// Find the target player"].join("\n");
    assert.deepEqual(frenchCommentLines("A.cs", code), [1, 2]);
});

test("leaves strings and English comments alone", () => {
    assert.deepEqual(frenchCommentLines("A.cs", 'Log("Déjà vu"); // checked'), []);
    assert.deepEqual(frenchCommentLines("build.ps1", "Write-Host 'Déjà vu' # checked"), []);
});

test("flags the glossary's Avoid words in comments, apart or joined", () => {
    const glossary = ["**Imitated Player**:", "_Avoid_: Source player, hidden player\r", "_Avoid_: Clip, droid"].join("\n");
    const code = [
        "// The game turns a hidden player's flashlight on",
        "// Clones the sourcePlayer rig",
        "// Hidden players stay muted",
        "// One droid per teammate",
        "var c = new DroidController(); // trims the clip",
        'Log("hidden player");',
    ].join("\n");
    assert.deepEqual(commentLinesMatching(avoidedWords(glossary), "A.cs", code), [1, 2, 3, 4]);
});

test("flags the glossary's Avoid words in C# strings", () => {
    const avoided = avoidedWords("_Avoid_: Source player, droid");
    const code = [
        'LOG.Info("no droid to drop");',
        'LOG.Debug($"Mimic of {sourcePlayer.playerName}");',
        'var name = $"Hallucination_{player}"; // no droid here',
        'LOG.Info("found \\"droids\\"");',
    ].join("\n");
    assert.deepEqual(stringLinesMatching(avoided, "A.cs", code), [1, 4]);
    assert.deepEqual(stringLinesMatching(avoided, "build.ps1", "Write-Host \"droid\""), []);
});
