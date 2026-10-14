// Prints a Unity prefab's hierarchy, one GameObject per line with its layer (unless Default) and its components:
//   node tooling/prefab-tree.mjs "../Repo_04/Assets/REPO/Game/Resources/PlayerAvatar.prefab"
//   --transforms      adds each object's local scale and its height above the root, rotations ignored
//   --refs <Script>   prints that script's references to other objects, by name, instead of the tree
// Scripts are named through the .cs.meta files under ../Repo_04/Assets (or the folder given second), layers through
// the ProjectSettings/TagManager.asset beside that folder.
import { existsSync, readdirSync, readFileSync } from "node:fs";
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
const vector = (body, name) =>
    body.match(new RegExp(`^\\s*${name}: \\{x: ([^,]+), y: ([^,]+), z: ([^}]+)\\}`, "m"))?.slice(1).map(Number);
const scriptGuid = (body) => body.match(/m_Script: \{fileID: -?\d+, guid: (\w+)/)?.[1];

function parseObjects(yaml) {
    const objects = new Map();
    for (const [, classId, id, body] of yaml.matchAll(/^--- !u!(\d+) &(-?\d+).*\r?\n([\s\S]*?)(?=^--- |(?![\s\S]))/gm)) {
        objects.set(id, { classId, body });
    }
    return objects;
}

/**
 * @param scriptNames Map from a script GUID to its class name.
 * @param layerNames Layer names by index; a layer without one prints as its number.
 */
export function prefabTree(yaml, scriptNames = new Map(), { layerNames = [], transforms: withTransforms = false } = {}) {
    const objects = parseObjects(yaml);

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
    const walk = (transformId, depth, parentHeight, parentScale) => {
        const transform = objects.get(transformId)?.body;
        const gameObject = transform && objects.get(fileId(transform, "m_GameObject"))?.body;
        if (!gameObject) return;
        const inactive = field(gameObject, "m_IsActive") === "0" ? " (inactive)" : "";
        const layer = field(gameObject, "m_Layer") ?? "0";
        const layerLabel = layer === "0" ? "" : ` layer=${layerNames[layer] || layer}`;
        const scale = vector(transform, "m_LocalScale") ?? [1, 1, 1];
        const height = parentHeight + parentScale * (vector(transform, "m_LocalPosition")?.[1] ?? 0);
        const transformLabel = withTransforms ? ` y≈${height.toFixed(2)} scale=(${scale.join(", ")})` : "";
        const components = (componentsOf.get(fileId(transform, "m_GameObject")) ?? []).join(", ");
        lines.push(
            `${"  ".repeat(depth)}${field(gameObject, "m_Name")}${inactive}${layerLabel}${transformLabel}${components && `  ${components}`}`,
        );
        for (const [, child] of (transform.match(/m_Children:\r?\n((?:\s*- \{fileID: -?\d+\}\r?\n)*)/)?.[1] ?? "").matchAll(
            /fileID: (-?\d+)/g,
        )) {
            walk(child, depth + 1, height, parentScale * scale[1]);
        }
    };
    for (const id of transforms.filter((id) => fileId(objects.get(id).body, "m_Father") === "0")) walk(id, 0, 0, 1);
    return lines;
}

function describeComponent(classId, body, scriptNames) {
    const guid = scriptGuid(body);
    let name = guid ? (scriptNames.get(guid) ?? `Script:${guid.slice(0, 8)}`) : (builtInNames[classId] ?? `Class${classId}`);
    const shadows = field(body, "m_CastShadows");
    if (shadows !== undefined) name += `[shadows:${shadowModes[shadows] ?? shadows}]`;
    if (field(body, "m_IsTrigger") === "1") name += "[trigger]";
    if (field(body, "m_Enabled") === "0") name += " (disabled)";
    return name;
}

/** Each object reference the named script holds, by field: `head → Collider (Transform)`, or a GameObject's name. */
export function componentRefs(yaml, scriptNames, scriptName) {
    const objects = parseObjects(yaml);
    const nameOf = (id) => field(objects.get(id)?.body ?? "", "m_Name");
    const lines = [];
    for (const { body } of objects.values()) {
        const guid = scriptGuid(body);
        if (!guid || scriptNames.get(guid) !== scriptName) continue;
        for (const [, name, id] of body.matchAll(/^ {2}(\w+): \{fileID: (-?\d+)\}/gm)) {
            const target = objects.get(id);
            if (!target || name === "m_GameObject") continue;
            const owner = fileId(target.body, "m_GameObject");
            if (!owner) {
                lines.push(`${name} → ${nameOf(id)}`);
                continue;
            }
            const guidOfTarget = scriptGuid(target.body);
            const kind = transformClasses.has(target.classId)
                ? "Transform"
                : guidOfTarget
                  ? (scriptNames.get(guidOfTarget) ?? `Script:${guidOfTarget.slice(0, 8)}`)
                  : (builtInNames[target.classId] ?? `Class${target.classId}`);
            lines.push(`${name} → ${nameOf(owner)} (${kind})`);
        }
    }
    return lines;
}

export function layerNamesIn(tagManager) {
    const block = tagManager.match(/^ {2}layers:\r?\n((?: {2}-.*\r?\n?)*)/m)?.[1] ?? "";
    return [...block.matchAll(/^ {2}-(.*)$/gm)].map(([, name]) => name.trim());
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
    const args = process.argv.slice(2);
    const take = (flag, withValue) => {
        const index = args.indexOf(flag);
        return index < 0 ? undefined : args.splice(index, withValue ? 2 : 1).at(-1);
    };
    const refs = take("--refs", true);
    const withTransforms = take("--transforms", false) !== undefined;
    const [prefab, assets = "../Repo_04/Assets"] = args;
    if (!prefab) {
        console.error("Usage: node tooling/prefab-tree.mjs <file.prefab> [assets folder] [--transforms] [--refs <Script>]");
        process.exit(1);
    }
    const yaml = readFileSync(prefab, "utf8");
    const scriptNames = scriptNamesUnder(assets);
    const tagManager = join(assets, "..", "ProjectSettings", "TagManager.asset");
    const layerNames = existsSync(tagManager) ? layerNamesIn(readFileSync(tagManager, "utf8")) : [];
    const lines = refs
        ? componentRefs(yaml, scriptNames, refs)
        : prefabTree(yaml, scriptNames, { layerNames, transforms: withTransforms });
    console.log(lines.join("\n"));
}
