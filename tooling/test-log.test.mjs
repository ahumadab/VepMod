import assert from "node:assert/strict";
import { test } from "node:test";
import { summarizeLog } from "./test-log.mjs";

const log = [
    "[Warning:Repo Steam Networking] RepoSteamNetworkManager already initialized!",
    "[Info   :VepMod.PlayerAvatarPatch] 23:01:02.788 Local player VieuxPoulpe, Master: True",
    "[Debug  :VepMod.HallucinationBodyPose] 23:01:54.147 Body pose of Hallucination_VieuxPoulpe: CheckingMap, Still",
    "[Warning:VepMod.HallucinationMovement] 23:09:55.776 Controller off NavMesh at (-10.08, 6.90, 7.54)",
    "[Warning:VepMod.HallucinationMovement] 23:09:55.793 Controller off NavMesh at (-10.08, 6.90, 7.54)",
    "[Warning:VepMod.HallucinationMovement] 23:09:55.810 Controller off NavMesh at (-10.08, 6.90, 7.54)",
    "[Info   :VepMod.HallucinationDevTools] 23:12:23.730 DevTools: 50 renderers of Hallucination_VieuxPoulpe",
    "[Error  : Unity Log] Can't remove AudioSource because AudioLowPassFilter depends on it",
    "[Error  : Unity Log] Can't remove AudioSource because AudioLowPassFilter depends on it",
    "[Error  :Imperium Repo] [NET] Trying to dispatch to clients from non-host. Blocked by Imperium policy.",
].join("\r\n");

test("keeps VepMod's lines and the game's errors, not another mod's, heads each level, folds repeats", () => {
    assert.deepEqual(summarizeLog(log), [
        "--- 23:01:02.788 level start, local player VieuxPoulpe, Master: True",
        "23:01:54.147 Debug HallucinationBodyPose: Body pose of Hallucination_VieuxPoulpe: CheckingMap, Still",
        "23:09:55.776 Warning HallucinationMovement: Controller off NavMesh at (-10.08, 6.90, 7.54) ×3",
        "23:12:23.730 HallucinationDevTools: DevTools: 50 renderers of Hallucination_VieuxPoulpe",
        "[Error  : Unity Log] Can't remove AudioSource because AudioLowPassFilter depends on it ×2",
    ]);
});

test("filters by the regex but keeps the level headers", () => {
    assert.deepEqual(summarizeLog(log, /Body pose/), [
        "--- 23:01:02.788 level start, local player VieuxPoulpe, Master: True",
        "23:01:54.147 Debug HallucinationBodyPose: Body pose of Hallucination_VieuxPoulpe: CheckingMap, Still",
    ]);
});

test("drops the lines the exclude regex matches", () => {
    assert.deepEqual(summarizeLog(log, undefined, /NavMesh|renderers|Can't remove/), [
        "--- 23:01:02.788 level start, local player VieuxPoulpe, Master: True",
        "23:01:54.147 Debug HallucinationBodyPose: Body pose of Hallucination_VieuxPoulpe: CheckingMap, Still",
    ]);
});

test("shows the game's errors of a launch that reached no level", () => {
    const failedLaunch = [
        "[Info   :    VepMod] com.vep.vepMod v1.1.0 has loaded!",
        "[Error  : Unity Log] Steamworks failed to initialize. Error: SteamApi_Init failed with NoSteamClient",
        "[Error  : Unity Log] NullReferenceException: Object reference not set to an instance of an object",
        "Stack trace:",
        "[Error  : Unity Log] NullReferenceException: Object reference not set to an instance of an object",
    ].join("\r\n");

    assert.deepEqual(summarizeLog(failedLaunch), [
        "no level start; the game's errors:",
        "[Error  : Unity Log] Steamworks failed to initialize. Error: SteamApi_Init failed with NoSteamClient",
        "[Error  : Unity Log] NullReferenceException: Object reference not set to an instance of an object ×2",
    ]);
});
