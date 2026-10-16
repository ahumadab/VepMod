// npm run release:finish [--dry-run]: tags the release merged into main, builds its zip, merges main into develop
// (AGENTS.md, Git workflow).
import { readFileSync } from "node:fs";
import { aheadBehind, fail, fetchOnCleanTree, git, run } from "./release.mjs";

if (git("branch", "--show-current") !== "main") {
    fail("Finish a release from main, once its PR is merged: git switch main, then git pull.");
}

fetchOnCleanTree();

if (aheadBehind("origin/main").some((count) => count !== 0)) {
    fail("main differs from origin/main: pull it, or reset it to origin/main.");
}

const { version } = JSON.parse(readFileSync("package.json", "utf8"));
if (git("tag", "--list", version) !== "") {
    fail(
        `${version} is already tagged: merge the release PR into main first. If release:finish stopped after tagging, merge main into develop by hand.`,
    );
}

// The zip is built before the tag goes out, so a missing Unity bundle leaves nothing published.
run("powershell", ["-ExecutionPolicy", "Bypass", "-File", "build-release.ps1", "-Version", version]);
run("git", ["tag", "--annotate", "--message", `Release ${version}`, version]);
run("git", ["push", "origin", version]);

run("git", ["switch", "develop"]);
run("git", ["merge", "--ff-only", "origin/develop"], "develop has commits origin/develop lacks: reset it to origin/develop, then merge main into it.");
run(
    "git",
    ["merge", "--no-ff", "--no-edit", "main"],
    "Merging main into develop conflicts: resolve the conflicts, run git commit, then git push origin develop.",
);
run("git", ["push", "origin", "develop"]);
console.log(`${version} is tagged and back in develop: upload dist/VieuxPoulpe-VepMod-${version}.zip to Thunderstore.`);
