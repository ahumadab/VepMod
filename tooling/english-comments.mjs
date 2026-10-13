import { extname } from "node:path";

// A heuristic, accepted as such: it only looks for French accented letters in comments.
const commentByExtension = { ".cs": /\/\/.*|^\s*\/?\*.*/, ".ps1": /#.*/ };
const french = /[éèêàùç]/i;

export const checkedExtensions = Object.keys(commentByExtension);

export function frenchCommentLines(file, text) {
    const comment = commentByExtension[extname(file)];
    return text
        .split("\n")
        .flatMap((line, index) => (french.test(line.match(comment)?.[0] ?? "") ? [index + 1] : []));
}
