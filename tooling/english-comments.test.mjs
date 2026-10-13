import assert from "node:assert/strict";
import { test } from "node:test";
import { frenchCommentLines } from "./english-comments.mjs";

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
