// Prints VepMod's lines and the game's errors from the BepInEx logs of the last in-game test, one section per instance
// and per level:
//   node tooling/test-log.mjs [profile] [--grep <regex>] [--exclude <regex>]
//   Regexes are case-sensitive: a case-blind STALK also matches every periodic [StalkApproach] body line.
// Reads the profile's LogOutput.log* (VieuxPoulpe by default) written within half an hour of the newest: a two-instance
// test writes two at once, and older files shift to .2, .3. The same message repeated in a row prints once, with ×count.
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const RecentWindow = 30 * 60 * 1000; // ms before the newest file

const vepModLine = /^\[(\w+)\s*:VepMod\.(\w+)\] (\S+) (.*)$/;
const levelStart = /Local player (.*), Master: (\w+)/;
// The game's errors reach BepInEx through Unity's log; another mod's carry its own source.
const gameError = /^\[(Error|Fatal)\s*: Unity Log\]/;

/**
 * VepMod's lines and the game's errors of one log, a header at each level start, consecutive repeats folded into
 * ×count. A log that reached no level gives the game's errors alone: a launch can fail before VepMod logs anything.
 */
export function summarizeLog(text, grep, exclude) {
    const lines = [];
    const errors = new Map();
    let levels = 0;
    let previous;
    let repeats = 0;
    const flush = () => {
        if (repeats > 1) lines[lines.length - 1] += ` ×${repeats}`;
        repeats = 0;
    };
    for (const line of text.split(/\r?\n/)) {
        let key;
        let printed;
        if (gameError.test(line)) {
            errors.set(line, (errors.get(line) ?? 0) + 1);
            key = printed = line;
        } else {
            const match = line.match(vepModLine);
            if (!match) continue;
            const [, level, source, time, message] = match;
            const start = levelStart.exec(message);
            if (start) {
                flush();
                previous = undefined;
                levels++;
                lines.push(`--- ${time} level start, local player ${start[1]}, Master: ${start[2]}`);
                continue;
            }
            key = `${level} ${source} ${message}`;
            printed = `${time} ${level === "Info" ? "" : `${level} `}${source}: ${message}`;
        }
        if ((grep && !grep.test(line)) || exclude?.test(line)) continue;
        if (key === previous) {
            repeats++;
            continue;
        }
        flush();
        previous = key;
        repeats = 1;
        lines.push(printed);
    }
    flush();
    if (levels === 0 && errors.size > 0) {
        return [
            "no level start; the game's errors:",
            ...[...errors].map(([line, count]) => (count > 1 ? `${line} ×${count}` : line)),
        ];
    }
    return lines;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const args = process.argv.slice(2);
    const grepIndex = args.indexOf("--grep");
    const grep = grepIndex < 0 ? undefined : new RegExp(args.splice(grepIndex, 2)[1]);
    const excludeIndex = args.indexOf("--exclude");
    const exclude = excludeIndex < 0 ? undefined : new RegExp(args.splice(excludeIndex, 2)[1]);
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
        console.log(summarizeLog(readFileSync(join(folder, name), "utf8"), grep, exclude).join("\n"));
    }
}
