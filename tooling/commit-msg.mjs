// A commit subject becomes the PR title, then a CHANGELOG entry: release-it parses it as a Conventional Commit, and it
// uses the GLOSSARY.md terms like a comment does.
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

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
    const subject = readFileSync(process.argv[2], "utf8").split("\n")[0].replace(/\r$/, "");
    const problem = subjectProblem(subject, readFileSync("GLOSSARY.md", "utf8"));
    if (problem) {
        console.error(problem);
        process.exit(1);
    }
}
