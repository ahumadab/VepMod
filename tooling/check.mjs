// The one check run by the git hooks and CI (AGENTS.md).
import { execFileSync, spawnSync } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";
import { checkedExtensions, frenchCommentLines } from "./english-comments.mjs";
import { touchedFiles } from "./touched-files.mjs";

const [mode, scope] = process.argv.slice(2);
const staged = scope === "--staged";
const quiet = ["-v", "q", "-clp:ErrorsOnly"];

function run(command, args, hint) {
    console.log(`> ${command} ${args.join(" ")}`);
    const { status } = spawnSync(command, args, { stdio: "inherit" });
    if (status === 0) return;
    if (hint) console.error(hint);
    process.exit(status ?? 1);
}

const base = staged
    ? undefined
    : execFileSync("git", ["merge-base", "HEAD", `origin/${process.env.GITHUB_BASE_REF || "develop"}`], {
          encoding: "utf8",
      }).trim();

function checkFormat(files) {
    // An empty --include would check every file.
    if (files.length === 0) return;
    const verify = mode === "fix" ? [] : ["--verify-no-changes"];
    run("dotnet", ["format", "whitespace", "--folder", ...verify, "--include", ...files]);
}

function checkEnglishComments(files) {
    const offenders = files.flatMap((file) =>
        frenchCommentLines(file, readFileSync(file, "utf8")).map((line) => `${file}:${line}`),
    );
    if (offenders.length === 0) return;
    console.error(`Comments are written in English (CODING_STANDARDS.md):\n  ${offenders.join("\n  ")}`);
    process.exit(1);
}

checkFormat(touchedFiles(["*.cs"], { staged, base }));
if (mode === "format" || mode === "fix") process.exit(0);

checkEnglishComments(touchedFiles(checkedExtensions.map((extension) => `*${extension}`), { staged, base }));
run("node", ["--test", "tooling/**/*.test.mjs"]);
run(
    "dotnet",
    ["build", "tooling/VepFrameworkIsolation/VepFrameworkIsolation.csproj", ...quiet],
    "VepFramework references code it must not: see docs/adr/0001-vepframework-is-a-reusable-library.md.",
);
// NoVad first, so bin/ is left with the default plugin. The copy into a game profile is a developer-machine step.
for (const noVad of ["true", "false"]) {
    const variant = [`-p:VepModNoVad=${noVad}`, "-p:CopyFilesToPluginOutputDirectoryOnBuild=false"];
    run("dotnet", ["build", "VepMod.csproj", "-c", "Debug", ...variant, ...quiet]);
}
for (const project of readdirSync("tests", { recursive: true }).filter((path) => path.endsWith(".csproj"))) {
    run("dotnet", ["test", `tests/${project}`, "-v", "q"]);
}
console.log("check: OK");
