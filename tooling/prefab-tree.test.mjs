import assert from "node:assert/strict";
import { test } from "node:test";
import { prefabTree } from "./prefab-tree.mjs";

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
