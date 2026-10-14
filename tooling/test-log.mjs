// Prints VepMod's lines from the BepInEx logs of the last in-game test, one section per instance and per level:
//   node tooling/test-log.mjs [profile] [--grep <regex>]
// Reads the profile's LogOutput.log* (VieuxPoulpe by default) written within half an hour of the newest: a two-instance
// test writes two at once, and older files shift to .2, .3. The same message repeated in a row prints once, with ×count.
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const RecentWindow = 30 * 60 * 1000; // ms before the newest file

const vepModLine = /^\[(\w+)\s*:VepMod\.(\w+)\] (\S+) (.*)$/;
const levelStart = /Local player (.*), Master: (\w+)/;

/** VepMod's lines of one log, a header at each level start, consecutive repeats folded into ×count. */
export function summarizeLog(text, grep) {
    const lines = [];
    let previous;
    let repeats = 0;
    const flush = () => {
        if (repeats > 1) lines[lines.length - 1] += ` ×${repeats}`;
        repeats = 0;
    };
    for (const line of text.split(/\r?\n/)) {
        const match = line.match(vepModLine);
        if (!match) continue;
        const [, level, source, time, message] = match;
        const start = levelStart.exec(message);
        if (start) {
            flush();
            previous = undefined;
            lines.push(`--- ${time} level start, local player ${start[1]}, Master: ${start[2]}`);
            continue;
        }
        if (grep && !grep.test(line)) continue;
        const key = `${level} ${source} ${message}`;
        if (key === previous) {
            repeats++;
            continue;
        }
        flush();
        previous = key;
        repeats = 1;
        lines.push(`${time} ${level === "Info" ? "" : `${level} `}${source}: ${message}`);
    }
    flush();
    return lines;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const args = process.argv.slice(2);
    const grepIndex = args.indexOf("--grep");
    const grep = grepIndex < 0 ? undefined : new RegExp(args.splice(grepIndex, 2)[1]);
    const profile = args[0] ?? "VieuxPoulpe";
    const folder = join(
        process.env.APPDATA,
        "Thunderstore Mod Manager", "DataFolder", "REPO", "profiles", profile, "BepInEx",
    );
    const logs = readdirSync(folder)
        .filter((name) => name.startsWith("LogOutput.log"))
        .map((name) => ({ name, modified: statSync(join(folder, name)).mtimeMs }))
        .sort((a, b) => b.modified - a.modified);
    for (const { name, modified } of logs.filter(({ modified }) => modified >= logs[0].modified - RecentWindow)) {
        console.log(`=== ${name}, written ${new Date(modified).toLocaleString()}`);
        console.log(summarizeLog(readFileSync(join(folder, name), "utf8"), grep).join("\n"));
    }
}
