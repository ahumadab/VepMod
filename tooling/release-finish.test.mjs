import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const releaseFinish = fileURLToPath(new URL("release-finish.mjs", import.meta.url));

function git(cwd, ...args) {
    return execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@t", ...args], { cwd, encoding: "utf8" });
}

function cloneOnMain(t) {
    const directory = mkdtempSync(join(tmpdir(), "release-finish-"));
    t.after(() => rmSync(directory, { recursive: true, force: true }));
    const clone = join(directory, "clone");
    git(directory, "init", "-q", "--bare", "origin.git");
    git(directory, "init", "-q", "--initial-branch=main", clone);
    git(clone, "remote", "add", "origin", join(directory, "origin.git"));
    writeFileSync(join(clone, "package.json"), JSON.stringify({ version: "1.1.0" }));
    git(clone, "add", ".");
    git(clone, "commit", "-q", "-m", "chore(version): increasing to 1.1.0");
    git(clone, "push", "-q", "-u", "origin", "main");
    return clone;
}

function releaseFinishIn(cwd) {
    return spawnSync(process.execPath, [releaseFinish, "--dry-run"], { cwd, encoding: "utf8" });
}

test("refuses a branch other than main", (t) => {
    const clone = cloneOnMain(t);
    git(clone, "switch", "-q", "-c", "develop");

    const run = releaseFinishIn(clone);

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /from main/);
});

test("refuses a main ahead of origin/main", (t) => {
    const clone = cloneOnMain(t);
    git(clone, "commit", "-q", "--allow-empty", "-m", "fix: unpushed");

    const run = releaseFinishIn(clone);

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /main differs from origin\/main/);
});

test("refuses a main behind origin/main", (t) => {
    const clone = cloneOnMain(t);
    git(clone, "commit", "-q", "--allow-empty", "-m", "fix: pushed");
    git(clone, "push", "-q", "origin", "main");
    git(clone, "reset", "-q", "--hard", "HEAD~1");

    const run = releaseFinishIn(clone);

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /main differs from origin\/main/);
});

test("refuses a version already tagged", (t) => {
    const clone = cloneOnMain(t);
    git(clone, "tag", "1.1.0");

    const run = releaseFinishIn(clone);

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /1\.1\.0 is already tagged/);
});

test("in a dry run, builds, tags and merges into develop only on paper", (t) => {
    const clone = cloneOnMain(t);
    git(clone, "push", "-q", "origin", "main:develop");

    const run = releaseFinishIn(clone);

    assert.equal(run.status, 0, run.stderr);
    assert.match(run.stdout, /Would run: powershell .*build-release\.ps1 -Version 1\.1\.0/);
    assert.match(run.stdout, /Would run: git push origin 1\.1\.0/);
    assert.match(run.stdout, /Would run: git merge --no-ff --no-edit main/);
    assert.equal(git(clone, "tag", "--list"), "");
    assert.equal(git(clone, "branch", "--show-current").trim(), "main");
});
