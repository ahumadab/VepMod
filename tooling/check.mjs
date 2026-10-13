// The one check run by the git hooks and CI. `node tooling/check.mjs` runs every step and stops at the first failure;
// `node tooling/check.mjs format --staged` only checks the formatting of the staged C# files.
import { execFileSync, spawnSync } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";

const [only, scope] = process.argv.slice(2);

function run(command, args, hint) {
    console.log(`> ${command} ${args.join(" ")}`);
    const { status } = spawnSync(command, args, { stdio: "inherit" });
    if (status === 0) return;
    if (hint) console.error(hint);
    process.exit(status ?? 1);
}

function gitLines(...args) {
    return execFileSync("git", args, { encoding: "utf8" }).split("\n").filter(Boolean);
}

// Existing code is brought to the rules when it is touched: only the C# files the change touches are checked.
function touchedCsFiles() {
    if (scope === "--staged") return gitLines("diff", "--cached", "--name-only", "--diff-filter=ACMR", "--", "*.cs");
    const [base] = gitLines("merge-base", "HEAD", `origin/${process.env.GITHUB_BASE_REF || "develop"}`);
    return gitLines("diff", "--name-only", "--diff-filter=ACMR", base, "--", "*.cs");
}

function checkFormat(files) {
    // An empty --include would check every file.
    if (files.length === 0) return;
    run("dotnet", ["format", "whitespace", "--folder", "--verify-no-changes", "--include", ...files]);
}

// A heuristic: French accented letters inside // or block comments.
function checkEnglishComments(files) {
    const comment = /\/\/.*|^\s*\/?\*.*/;
    const french = /[éèêàùç]/i;
    const offenders = files.flatMap((file) =>
        readFileSync(file, "utf8")
            .split("\n")
            .map((line, index) => (french.test(line.match(comment)?.[0] ?? "") ? `${file}:${index + 1}` : null))
            .filter(Boolean),
    );
    if (offenders.length === 0) return;
    console.error(`Comments are written in English (CODING_STANDARDS.md):\n  ${offenders.join("\n  ")}`);
    process.exit(1);
}

const files = touchedCsFiles();
checkFormat(files);
if (only === "format") process.exit(0);

checkEnglishComments(files);
run(
    "dotnet",
    ["build", "tooling/VepFrameworkIsolation/VepFrameworkIsolation.csproj", "-v", "q", "-clp:ErrorsOnly"],
    "ADR-0001: VepFramework may only reference BepInEx, Unity and its own libraries, never the game, Photon or Whispral.",
);
// NoVad first, so bin/ is left with the default plugin. The copy into a game profile is a developer-machine step.
for (const noVad of ["true", "false"]) {
    run("dotnet", [
        "build",
        "VepMod.csproj",
        "-c",
        "Debug",
        `-p:VepModNoVad=${noVad}`,
        "-p:CopyFilesToPluginOutputDirectoryOnBuild=false",
        "-v",
        "q",
        "-clp:ErrorsOnly",
    ]);
}
for (const project of readdirSync("tests", { recursive: true }).filter((path) => path.endsWith(".csproj"))) {
    run("dotnet", ["test", `tests/${project}`, "-v", "q"]);
}
