// release-it's after:bump hook (.release-it.yml): writes the version where BepInEx and Thunderstore read it. Node, not
// PowerShell 5.1, whose Set-Content -Encoding UTF8 adds the BOM dotnet format refuses.
import { readFileSync, writeFileSync } from "node:fs";

const [version] = process.argv.slice(2);

function replaceVersion(file, pattern) {
    const text = readFileSync(file, "utf8");
    if (!pattern.test(text)) throw new Error(`${file} has no version matching ${pattern}.`);
    writeFileSync(file, text.replace(pattern, `$1${version}`));
}

replaceVersion("src/VepMod.cs", /(\[BepInPlugin\("com\.vep\.vepMod", "VepMod", ")[^"]+/);
replaceVersion("thunderstore/manifest.json", /("version_number": ")[^"]+/);
