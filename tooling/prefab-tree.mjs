// Prints a Unity prefab's hierarchy, one GameObject per line with its components:
//   node tooling/prefab-tree.mjs "../Repo_04/Assets/REPO/Game/Resources/PlayerAvatar.prefab"
// Scripts are named through the .cs.meta files under ../Repo_04/Assets (or the folder given second).
import { readdirSync, readFileSync } from "node:fs";
import { basename, join } from "node:path";
import { fileURLToPath } from "node:url";

// Unity's class IDs for the built-in components a prefab holds.
const builtInNames = {
    20: "Camera",
    23: "MeshRenderer",
    33: "MeshFilter",
    54: "Rigidbody",
    59: "HingeJoint",
    64: "MeshCollider",
    65: "BoxCollider",
    82: "AudioSource",
    95: "Animator",
    96: "TrailRenderer",
    108: "Light",
    111: "Animation",
    120: "LineRenderer",
    122: "Halo",
    135: "SphereCollider",
    136: "CapsuleCollider",
    137: "SkinnedMeshRenderer",
    153: "ConfigurableJoint",
    169: "AudioLowPassFilter",
    198: "ParticleSystem",
    199: "ParticleSystemRenderer",
    212: "SpriteRenderer",
    222: "CanvasRenderer",
    223: "Canvas",
};

const transformClasses = new Set(["4", "224"]);
const shadowModes = ["Off", "On", "TwoSided", "ShadowsOnly"];

const field = (body, name) => body.match(new RegExp(`^\\s*${name}: (.*)$`, "m"))?.[1];
const fileId = (body, name) => body.match(new RegExp(`^\\s*${name}: \\{fileID: (-?\\d+)`, "m"))?.[1];

/** @param scriptNames Map from a script GUID to its class name. */
export function prefabTree(yaml, scriptNames = new Map()) {
    const objects = new Map();
    for (const [, classId, id, body] of yaml.matchAll(/^--- !u!(\d+) &(-?\d+).*\n([\s\S]*?)(?=^--- |(?![\s\S]))/gm)) {
        objects.set(id, { classId, body });
    }

    const componentsOf = new Map();
    const transforms = [];
    for (const [id, { classId, body }] of objects) {
        if (classId === "1") continue;
        if (transformClasses.has(classId)) {
            transforms.push(id);
            continue;
        }
        const owner = fileId(body, "m_GameObject");
        if (!owner) continue;
        componentsOf.set(owner, [...(componentsOf.get(owner) ?? []), describeComponent(classId, body, scriptNames)]);
    }

    const lines = [];
    const walk = (transformId, depth) => {
        const transform = objects.get(transformId)?.body;
        const gameObject = transform && objects.get(fileId(transform, "m_GameObject"))?.body;
        if (!gameObject) return;
        const inactive = field(gameObject, "m_IsActive") === "0" ? " (inactive)" : "";
        const components = (componentsOf.get(fileId(transform, "m_GameObject")) ?? []).join(", ");
        lines.push(`${"  ".repeat(depth)}${field(gameObject, "m_Name")}${inactive}${components && `  ${components}`}`);
        for (const [, child] of (transform.match(/m_Children:\n((?:\s*- \{fileID: -?\d+\}\n)*)/)?.[1] ?? "").matchAll(
            /fileID: (-?\d+)/g,
        )) {
            walk(child, depth + 1);
        }
    };
    for (const id of transforms.filter((id) => fileId(objects.get(id).body, "m_Father") === "0")) walk(id, 0);
    return lines;
}

function describeComponent(classId, body, scriptNames) {
    const guid = body.match(/m_Script: \{fileID: -?\d+, guid: (\w+)/)?.[1];
    let name = guid ? (scriptNames.get(guid) ?? `Script:${guid.slice(0, 8)}`) : (builtInNames[classId] ?? `Class${classId}`);
    const shadows = field(body, "m_CastShadows");
    if (shadows !== undefined) name += `[shadows:${shadowModes[shadows] ?? shadows}]`;
    if (field(body, "m_Enabled") === "0") name += " (disabled)";
    return name;
}

export function scriptNamesUnder(folder) {
    const names = new Map();
    for (const path of readdirSync(folder, { recursive: true })) {
        if (!path.endsWith(".cs.meta")) continue;
        const guid = readFileSync(join(folder, path), "utf8").match(/^guid: (\w+)/m)?.[1];
        if (guid) names.set(guid, basename(path, ".cs.meta"));
    }
    return names;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const [prefab, assets = "../Repo_04/Assets"] = process.argv.slice(2);
    if (!prefab) {
        console.error("Usage: node tooling/prefab-tree.mjs <file.prefab> [assets folder]");
        process.exit(1);
    }
    console.log(prefabTree(readFileSync(prefab, "utf8"), scriptNamesUnder(assets)).join("\n"));
}
