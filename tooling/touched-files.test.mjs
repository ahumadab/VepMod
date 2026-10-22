import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { touchedFiles } from "./touched-files.mjs";

test("lists changed and untracked files, not ignored ones", (t) => {
    const cwd = mkdtempSync(join(tmpdir(), "touched-files-"));
    t.after(() => rmSync(cwd, { recursive: true, force: true }));
    const git = (...args) => execFileSync("git", args, { cwd });
    git("init", "-q");
    writeFileSync(join(cwd, ".gitignore"), "Ignored.cs\n");
    writeFileSync(join(cwd, "Changed.cs"), "");
    writeFileSync(join(cwd, "Same.cs"), "");
    git("add", ".");
    git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "base");
    writeFileSync(join(cwd, "Changed.cs"), "class A {}");
    writeFileSync(join(cwd, "New.cs"), "");
    writeFileSync(join(cwd, "Ignored.cs"), "");

    assert.deepEqual(touchedFiles(["*.cs"], { base: "HEAD", cwd }), ["Changed.cs", "New.cs"]);
});
