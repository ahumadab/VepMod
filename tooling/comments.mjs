import { extname } from "node:path";

const commentByExtension = { ".cs": /\/\/.*|^\s*\/?\*.*/, ".ps1": /#.*/ };

// A heuristic, accepted as such: French accented letters, or French function words that English never uses alone.
export const french = /[àâçéèêëîïôùûœ]|\b(le|la|les|des|du|une|pour|dans|avec|pas|est)\b/i;

// Too common in comments with another meaning (a target framerate, a Unity clone) to flag without a reviewer.
const tooCommon = new Set(["target", "host", "clone", "clip", "recording", "extract"]);

export const checkedExtensions = Object.keys(commentByExtension);

// Matches the words GLOSSARY.md lists under "Avoid", written apart or joined (`source player`, `sourcePlayer`).
export function avoidedWords(glossary) {
    const terms = [...glossary.matchAll(/^_Avoid_: (.+?)\r?$/gm)]
        .flatMap((match) => match[1].split(","))
        .map((term) => term.trim().toLowerCase())
        .filter((term) => !tooCommon.has(term));
    return new RegExp(`\\b(${terms.map((term) => term.split(/\s+/).join("[ _]?")).join("|")})s?\\b`, "i");
}

export function commentLinesMatching(pattern, file, text) {
    const comment = commentByExtension[extname(file)];
    return text
        .split("\n")
        .flatMap((line, index) => (pattern.test(line.match(comment)?.[0] ?? "") ? [index + 1] : []));
}
