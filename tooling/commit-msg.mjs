// A commit subject becomes the PR title, then a CHANGELOG entry: release-it parses it as a Conventional Commit, and it
// uses the GLOSSARY.md terms like a comment does.
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { avoidedWords } from "./comments.mjs";

const conventional = /^(?:Merge |Revert "|(?:feat|fix|refactor|docs|chore|test|perf|build|ci|style|revert)(?:\([\w.-]+\))?!?: \S)/;

export function subjectProblem(subject, glossary) {
    if (!conventional.test(subject)) {
        return `The commit subject "${subject}" is not a Conventional Commit: write "<type>(<scope>): <subject>".`;
    }
    const avoided = subject.match(avoidedWords(glossary));
    if (avoided) return `The commit subject says "${avoided[0]}", a word GLOSSARY.md lists under Avoid: use its term.`;
    return null;
}

// An RPC's name and parameters are a wire contract (CODING_STANDARDS.md): release-it lists a break in the CHANGELOG, and
// bumps the major version, only from a BREAKING CHANGE footer.
export function rpcBreakProblem(stagedDiff, message) {
    if (/^BREAKING CHANGE: /m.test(message)) return null;
    let inSignature = false;
    for (const line of stagedDiff.split("\n")) {
        if (!line.startsWith(" ") && !line.startsWith("-")) continue;
        if (line.startsWith("--- ")) continue;
        if (line.startsWith("-") && (inSignature || line.includes("[PunRPC]"))) {
            return `This commit changes or removes a [PunRPC] method (${line.slice(1).trim()}): end the message with a "BREAKING CHANGE: <what breaks>" footer.`;
        }
        if (line.includes("[PunRPC]")) inSignature = true;
        else if (inSignature && line.includes(")")) inSignature = false;
    }
    return null;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
    const message = readFileSync(process.argv[2], "utf8");
    const subject = message.split("\n")[0].replace(/\r$/, "");
    const stagedDiff = execFileSync("git", ["diff", "--cached", "--", "*.cs"], { encoding: "utf8" });
    const problem =
        subjectProblem(subject, readFileSync("GLOSSARY.md", "utf8")) ??
        (subject.startsWith("Merge ") ? null : rpcBreakProblem(stagedDiff, message));
    if (problem) {
        console.error(problem);
        process.exit(1);
    }
}
