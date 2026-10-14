import assert from "node:assert/strict";
import { test } from "node:test";
import { rpcBreakProblem, subjectProblem } from "./commit-msg.mjs";

const glossary = "_Avoid_: Droid, debuff";

test("accepts Conventional Commit subjects and git's own merge and revert subjects", () => {
    for (const subject of [
        "feat(recorder): Voice Samples from voice frames (#42)",
        "fix: keep the save loop alive",
        "chore(tooling)!: drop Node 18",
        "Merge branch 'release/1.2.0' into develop",
        'Revert "feat(recorder): Voice Samples"',
    ]) {
        assert.equal(subjectProblem(subject, glossary), null, subject);
    }
});

test("rejects subjects release-it cannot parse", () => {
    for (const subject of ["feat(recorder) : space before the colon", "Adds the recorder", "feature(recorder): x"]) {
        assert.match(subjectProblem(subject, glossary), /Conventional Commit/, subject);
    }
});

test("rejects the glossary's Avoid words", () => {
    assert.match(subjectProblem("fix(haunting): the droid stops", glossary), /Avoid/);
});

const renamedRpc = `diff --git a/src/A.cs b/src/A.cs
--- a/src/A.cs
+++ b/src/A.cs
@@ -10,4 +10,4 @@
     [PunRPC]
-    public void ReceiveSharedAudioChunk(byte[] chunk, int index, string nickname)
+    public void ReceiveVoiceSampleChunk(byte[] chunk, int index)
     {
`;

test("asks a commit that changes a [PunRPC] signature for a BREAKING CHANGE footer", () => {
    assert.match(rpcBreakProblem(renamedRpc, "feat(sharing): rename\n"), /BREAKING CHANGE/);
    assert.equal(rpcBreakProblem(renamedRpc, "feat(sharing): rename\n\nBREAKING CHANGE: the RPC is renamed.\n"), null);
});

test("finds a parameter changed on a later line of a [PunRPC] signature, and a removed [PunRPC]", () => {
    const laterLine = `@@ -1,4 +1,4 @@
     [PunRPC]
     public void Play(int viewId,
-        string nickname)
+        int imitatedViewId)
`;
    const removed = `@@ -1,3 +1,0 @@
-    [PunRPC]
-    public void Play(int viewId)
-    {
`;
    assert.notEqual(rpcBreakProblem(laterLine, "fix: x\n"), null);
    assert.notEqual(rpcBreakProblem(removed, "fix: x\n"), null);
});

test("lets a new RPC or a change to an RPC's body through", () => {
    const added = `@@ -1,0 +1,3 @@
+    [PunRPC]
+    public void ReceiveVoiceSampleChunk(byte[] chunk)
+    {
`;
    const body = `@@ -1,5 +1,5 @@
     [PunRPC]
     public void Play(int viewId)
     {
-        Log(viewId);
+        LOG.Debug(viewId);
`;
    assert.equal(rpcBreakProblem(added, "feat: x\n"), null);
    assert.equal(rpcBreakProblem(body, "fix: x\n"), null);
});
