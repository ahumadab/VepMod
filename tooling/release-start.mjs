// npm run release:start [version] [--dry-run] (AGENTS.md, Git workflow).
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { aheadBehind, dryRun, fail, fetchOnCleanTree, git, gitSucceeds, run } from "./release.mjs";

const requested = process.argv.slice(2).filter((arg) => !arg.startsWith("--"));

// release-it's bin is a .cmd shim on Windows, which spawnSync cannot run without a shell. It runs in a dry run too, with
// its own --dry-run.
function releaseIt(args, { capture = false } = {}) {
    const bin = fileURLToPath(new URL("../node_modules/release-it/bin/release-it.js", import.meta.url));
    const { status, stdout } = spawnSync(process.execPath, [bin, ...args], {
        encoding: "utf8",
        stdio: ["inherit", capture ? "pipe" : "inherit", "inherit"],
    });
    if (status !== 0) process.exit(status ?? 1);
    return stdout;
}

const branch = git("branch", "--show-current");
const hotfix = branch.startsWith("hotfix/");
if (branch !== "develop" && !hotfix) {
    fail(
        `Start a release from develop, or a hotfix from hotfix/<slug> cut from main, not from ${branch || "a detached HEAD"}.`,
    );
}

fetchOnCleanTree();

if (hotfix) {
    if (!gitSucceeds("merge-base", "--is-ancestor", "origin/main", "HEAD")) {
        fail(`${branch} lacks origin/main: cut the hotfix from main, or merge origin/main into it.`);
    }
    const remote = `origin/${branch}`;
    if (git("branch", "--remotes", "--list", remote) !== "" && aheadBehind(remote)[1] !== 0) {
        fail(`${branch} is behind ${remote}: pull it first.`);
    }
} else {
    const [ahead, behind] = aheadBehind("origin/develop");
    if (behind !== 0) fail("develop is behind origin/develop: pull it first.");
    if (ahead !== 0) {
        fail("develop has commits origin/develop lacks: reset it to origin/develop, and bring them in through a PR.");
    }
}

// The release branch is named before release-it runs on it, so release-it is asked for the version first.
const version = releaseIt([...requested, "--release-version", "--ci", "--no-git.requireBranch"], { capture: true })
    .trim()
    .split("\n")
    .at(-1);
const releaseBranch = hotfix ? branch : `release/${version}`;
if (!hotfix && git("branch", "--all", "--list", releaseBranch, `origin/${releaseBranch}`) !== "") {
    fail(
        `${releaseBranch} already exists: finish that release, or delete the branch (git branch -D ${releaseBranch}, git push origin --delete ${releaseBranch}) and start again.`,
    );
}

console.log(`Releasing ${version} from ${releaseBranch}.`);
if (!hotfix) run("git", ["switch", "--create", releaseBranch]);
// A dry run stays on develop, which git.requireBranch refuses, and release-it's own dry run still runs npm version,
// which writes package.json.
releaseIt([version, "--ci", ...(dryRun ? ["--dry-run", "--no-git.requireBranch", "--no-npm"] : [])]);

// A squash merge leaves develop's commits out of main, so the two would diverge for good.
const mergeAsCommit =
    "Merge this PR with a merge commit, never a squash: npm run release:finish then merges main back into develop.";
run("gh", ["pr", "create", "--base", "main", "--fill", "--body", mergeAsCommit]);
console.log(`${mergeAsCommit}\nOnce it is merged, run npm run release:finish from an up-to-date main.`);
