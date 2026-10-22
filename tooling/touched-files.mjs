import { execFileSync } from "node:child_process";

function gitLines(cwd, ...args) {
    return execFileSync("git", args, { cwd, encoding: "utf8" }).split("\n").filter(Boolean);
}

// Formatting and comment rules reach existing code when it is touched; the analyzers fail the whole build. A file
// never added to git is touched too: a plain diff leaves it out until it is staged.
export function touchedFiles(patterns, { staged = false, base, cwd } = {}) {
    if (staged) return gitLines(cwd, "diff", "--cached", "--name-only", "--diff-filter=ACMR", "--", ...patterns);
    const changed = gitLines(cwd, "diff", "--name-only", "--diff-filter=ACMR", base, "--", ...patterns);
    const untracked = gitLines(cwd, "ls-files", "--others", "--exclude-standard", "--", ...patterns);
    return [...changed, ...untracked];
}
