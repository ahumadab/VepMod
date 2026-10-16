// What release-start.mjs and release-finish.mjs share (AGENTS.md, Git workflow).
import { execFileSync, spawnSync } from "node:child_process";

// npm keeps a --dry-run for itself and hands it to the script as npm_config_dry_run.
export const dryRun = process.argv.includes("--dry-run") || process.env.npm_config_dry_run === "true";

export function fail(message) {
    console.error(message);
    process.exit(1);
}

export function git(...args) {
    return execFileSync("git", args, { encoding: "utf8" }).trim();
}

export function succeeds(...gitArgs) {
    return spawnSync("git", gitArgs).status === 0;
}

export function run(command, args, hint) {
    if (dryRun) {
        console.log(`Would run: ${command} ${args.join(" ")}`);
        return;
    }
    console.log(`> ${command} ${args.join(" ")}`);
    const { status } = spawnSync(command, args, { stdio: "inherit" });
    if (status === 0) return;
    if (hint) console.error(hint);
    process.exit(status ?? 1);
}

export function fetchOnCleanTree() {
    if (git("status", "--porcelain") !== "") fail("The working tree has uncommitted changes: commit or stash them.");
    console.log("Fetching origin…");
    git("fetch", "--quiet", "--tags", "origin");
}

export function aheadBehind(remote) {
    return git("rev-list", "--left-right", "--count", `HEAD...${remote}`).split(/\s+/).map(Number);
}
