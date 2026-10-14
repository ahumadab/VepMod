// The one check run by the git hooks and CI (AGENTS.md).
import { execFileSync, spawnSync } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";
import { relative } from "node:path";
import { avoidedWords, checkedExtensions, commentLinesMatching, french } from "./comments.mjs";
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

// With TreatWarningsAsErrors, every warning left is a rule the csproj demotes (WarningsNotAsErrors) because existing
// code still breaks it: a touched file keeps none. A rebuild, since an up-to-date build reports no warning.
function buildWithoutWarningsIn(files, args) {
    const command = ["build", ...args, "--no-incremental", "-v", "q", "-clp:ErrorsOnly;WarningsOnly"];
    console.log(`> dotnet ${command.join(" ")}`);
    const { status, stdout } = spawnSync("dotnet", command, { encoding: "utf8" });
    if (status !== 0) {
        console.log(stdout);
        process.exit(status ?? 1);
    }
    const touched = new Set(files);
    const warnings = new Set(
        stdout.split("\n").flatMap((line) => {
            const match = line.match(/^\s*(.+?\.cs)\((\d+),\d+\): warning (\w+): (.+?)(?: \(https?:[^)]*\))? \[/);
            const file = match && relative(process.cwd(), match[1]).replaceAll("\\", "/");
            return file && touched.has(file) ? [`${file}:${match[2]} ${match[3]} ${match[4]}`] : [];
        }),
    );
    if (warnings.size === 0) return;
    console.error(`A touched file keeps no analyzer warning:\n  ${[...warnings].join("\n  ")}`);
    process.exit(1);
}

function checkComments(files, pattern, rule) {
    const offenders = files.flatMap((file) =>
        commentLinesMatching(pattern, file, readFileSync(file, "utf8")).map((line) => `${file}:${line}`),
    );
    if (offenders.length === 0) return;
    console.error(`${rule}:\n  ${offenders.join("\n  ")}`);
    process.exit(1);
}

const touchedCode = touchedFiles(["*.cs"], { staged, base });
checkFormat(touchedCode);
if (mode === "format" || mode === "fix") process.exit(0);

const commentedFiles = touchedFiles(checkedExtensions.map((extension) => `*${extension}`), { staged, base });
checkComments(commentedFiles, french, "Comments are written in English (CODING_STANDARDS.md)");
checkComments(
    commentedFiles,
    avoidedWords(readFileSync("GLOSSARY.md", "utf8")),
    "Comments use the GLOSSARY.md terms, not the words it lists under Avoid",
);
run("node", ["--test", "tooling/**/*.test.mjs"]);
run(
    "dotnet",
    ["build", "tooling/VepFrameworkIsolation/VepFrameworkIsolation.csproj", ...quiet],
    "VepFramework references code it must not: see docs/adr/0001-vepframework-is-a-reusable-library.md.",
);
// NoVad first, so bin/ is left with the default plugin. The copy into a game profile is a developer-machine step.
for (const noVad of ["true", "false"]) {
    const variant = [`-p:VepModNoVad=${noVad}`, "-p:CopyFilesToPluginOutputDirectoryOnBuild=false"];
    buildWithoutWarningsIn(touchedCode, ["VepMod.csproj", "-c", "Debug", ...variant]);
}
for (const project of readdirSync("tests", { recursive: true }).filter((path) => path.endsWith(".csproj"))) {
    run("dotnet", ["test", `tests/${project}`, "-v", "q"]);
}
console.log("check: OK");
