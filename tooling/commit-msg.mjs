// A commit subject becomes the PR title, then a CHANGELOG entry: it uses the GLOSSARY.md terms like a comment does.
import { readFileSync } from "node:fs";
import { avoidedWords } from "./comments.mjs";

const subject = readFileSync(process.argv[2], "utf8").split("\n")[0];
const avoided = subject.match(avoidedWords(readFileSync("GLOSSARY.md", "utf8")));
if (avoided) {
    console.error(`The commit subject says "${avoided[0]}", a word GLOSSARY.md lists under Avoid: use its term.`);
    process.exit(1);
}
