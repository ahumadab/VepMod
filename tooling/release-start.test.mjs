import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const releaseStart = fileURLToPath(new URL("release-start.mjs", import.meta.url));

function git(cwd, ...args) {
    return execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@t", ...args], { cwd, encoding: "utf8" });
}

// A clone whose develop and main equal origin's, main holding the 1.0.0 tag.
function cloneOnDevelop(t) {
    const directory = mkdtempSync(join(tmpdir(), "release-start-"));
    t.after(() => rmSync(directory, { recursive: true, force: true }));
    const clone = join(directory, "clone");
    git(directory, "init", "-q", "--bare", "origin.git");
    git(directory, "init", "-q", "--initial-branch=main", clone);
    git(clone, "remote", "add", "origin", join(directory, "origin.git"));
    git(clone, "commit", "-q", "--allow-empty", "-m", "chore: init");
    git(clone, "tag", "1.0.0");
    git(clone, "push", "-q", "-u", "origin", "main", "1.0.0");
    git(clone, "switch", "-q", "-c", "develop");
    git(clone, "commit", "-q", "--allow-empty", "-m", "feat: a feature");
    git(clone, "push", "-q", "-u", "origin", "develop");
    return clone;
}

function releaseStartIn(cwd, ...args) {
    return spawnSync(process.execPath, [releaseStart, ...args, "--dry-run"], { cwd, encoding: "utf8" });
}

test("refuses a branch other than develop or hotfix/*", (t) => {
    const clone = cloneOnDevelop(t);
    git(clone, "switch", "-q", "-c", "feat/x");

    const run = releaseStartIn(clone, "1.1.0");

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /from develop, or a hotfix from hotfix\/<slug>.*not from feat\/x/);
});

test("refuses a dirty tree", (t) => {
    const clone = cloneOnDevelop(t);
    writeFileSync(join(clone, "dirty.txt"), "");

    const run = releaseStartIn(clone, "1.1.0");

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /uncommitted changes/);
});

test("refuses a develop behind origin/develop", (t) => {
    const clone = cloneOnDevelop(t);
    git(clone, "commit", "-q", "--allow-empty", "-m", "feat: another");
    git(clone, "push", "-q", "origin", "develop");
    git(clone, "reset", "-q", "--hard", "HEAD~1");

    const run = releaseStartIn(clone, "1.1.0");

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /behind origin\/develop: pull it/);
});

test("refuses a develop ahead of origin/develop", (t) => {
    const clone = cloneOnDevelop(t);
    git(clone, "commit", "-q", "--allow-empty", "-m", "feat: unpushed");

    const run = releaseStartIn(clone, "1.1.0");

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /origin\/develop lacks/);
});

test("refuses a version whose release branch exists", (t) => {
    const clone = cloneOnDevelop(t);
    git(clone, "branch", "release/1.1.0");

    const run = releaseStartIn(clone, "1.1.0");

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /release\/1\.1\.0 already exists/);
});

test("refuses a hotfix branch not cut from main", (t) => {
    const clone = cloneOnDevelop(t);
    git(clone, "switch", "-q", "--orphan", "hotfix/x");
    git(clone, "commit", "-q", "--allow-empty", "-m", "fix: x");

    const run = releaseStartIn(clone, "1.0.1");

    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /hotfix\/x lacks origin\/main/);
});
