import { extname } from "node:path";

// A heuristic, accepted as such: French accented letters, or French function words that English never uses alone.
const commentByExtension = { ".cs": /\/\/.*|^\s*\/?\*.*/, ".ps1": /#.*/ };
const french = /[àâçéèêëîïôùûœ]|\b(le|la|les|des|du|une|pour|dans|avec|pas|est)\b/i;

export const checkedExtensions = Object.keys(commentByExtension);

export function frenchCommentLines(file, text) {
    const comment = commentByExtension[extname(file)];
    return text
        .split("\n")
        .flatMap((line, index) => (french.test(line.match(comment)?.[0] ?? "") ? [index + 1] : []));
}
