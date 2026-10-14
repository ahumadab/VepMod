import assert from "node:assert/strict";
import { test } from "node:test";
import { avoidedWords, commentLinesMatching, french } from "./comments.mjs";

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
