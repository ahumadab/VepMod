import assert from "node:assert/strict";
import { test } from "node:test";
import { join, resolve } from "node:path";
import { componentRefs, enclosingAssets, prefabTree } from "./prefab-tree.mjs";

const prefab = `%YAML 1.1
--- !u!1 &10
GameObject:
  m_Component:
  - component: {fileID: 11}
  m_Name: Root
  m_IsActive: 1
--- !u!4 &11
Transform:
  m_GameObject: {fileID: 10}
  m_Children:
  - {fileID: 21}
  m_Father: {fileID: 0}
--- !u!114 &12
MonoBehaviour:
  m_GameObject: {fileID: 10}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: abc123, type: 3}
--- !u!1 &20
GameObject:
  m_Name: Mesh Shadows
  m_IsActive: 0
--- !u!4 &21
Transform:
  m_GameObject: {fileID: 20}
  m_Children: []
  m_Father: {fileID: 11}
--- !u!23 &22
MeshRenderer:
  m_GameObject: {fileID: 20}
  m_Enabled: 0
  m_CastShadows: 3
--- !u!108 &23
Light:
  m_GameObject: {fileID: 20}
  m_Enabled: 1
`;

test("prints each GameObject under its parent with its components", () => {
    assert.deepEqual(prefabTree(prefab, new Map([["abc123", "FlashlightController"]])), [
        "Root  FlashlightController",
        "  Mesh Shadows (inactive)  MeshRenderer[shadows:ShadowsOnly] (disabled), Light",
    ]);
});

test("names a script it cannot resolve by its GUID", () => {
    assert.equal(prefabTree(prefab)[0], "Root  Script:abc123");
});

const physics = `%YAML 1.1
--- !u!1 &10
GameObject:
  m_Layer: 0
  m_Name: Root
  m_IsActive: 1
--- !u!4 &11
Transform:
  m_GameObject: {fileID: 10}
  m_LocalPosition: {x: 0, y: 1, z: 0}
  m_LocalScale: {x: 2, y: 2, z: 2}
  m_Children:
  - {fileID: 21}
  m_Father: {fileID: 0}
--- !u!114 &12
MonoBehaviour:
  m_GameObject: {fileID: 10}
  m_Script: {fileID: 11500000, guid: abc123, type: 3}
  head: {fileID: 21}
  body: {fileID: 20}
  missing: {fileID: 0}
--- !u!1 &20
GameObject:
  m_Layer: 16
  m_Name: Collider
  m_IsActive: 1
--- !u!4 &21
Transform:
  m_GameObject: {fileID: 20}
  m_LocalPosition: {x: 0, y: 0.5, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 11}
--- !u!65 &22
BoxCollider:
  m_GameObject: {fileID: 20}
  m_IsTrigger: 1
`;

const layerNames = Array.from({ length: 17 }, (_, i) => (i === 16 ? "PhysGrabObject" : ""));

test("names a GameObject's layer when it is not Default, and marks triggers", () => {
    assert.equal(prefabTree(physics, new Map(), { layerNames })[1], "  Collider layer=PhysGrabObject  BoxCollider[trigger]");
});

test("numbers a layer it cannot name", () => {
    assert.equal(prefabTree(physics)[1], "  Collider layer=16  BoxCollider[trigger]");
});

test("adds each Transform's local scale and its height, rotations ignored", () => {
    assert.deepEqual(prefabTree(physics, new Map(), { transforms: true }), [
        "Root y≈1.00 scale=(2, 2, 2)  Script:abc123",
        "  Collider layer=16 y≈2.00 scale=(1, 1, 1)  BoxCollider[trigger]",
    ]);
});

test("names a constraint and the objects it follows", () => {
    const constrained = `${physics}--- !u!1773428102 &13
ParentConstraint:
  m_GameObject: {fileID: 10}
  m_Enabled: 1
  m_Sources:
  - sourceTransform: {fileID: 21}
    weight: 1
`;
    assert.equal(prefabTree(constrained)[0], "Root  Script:abc123, ParentConstraint ← Collider");
});

test("resolves a component's references to GameObject names", () => {
    assert.deepEqual(componentRefs(physics, new Map([["abc123", "PlayerExpression"]]), "PlayerExpression"), [
        "head → Collider (Transform)",
        "body → Collider",
    ]);
});

test("finds the Assets folder holding the prefab", () => {
    const root = resolve("Repo0.5.0");
    assert.equal(
        enclosingAssets(join(root, "Assets", "REPO", "Game", "Resources", "PlayerAvatar.prefab")),
        join(root, "Assets"),
    );
    assert.equal(enclosingAssets(join(root, "PlayerAvatar.prefab")), undefined);
});
