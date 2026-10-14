import assert from "node:assert/strict";
import { test } from "node:test";
import { subjectProblem } from "./commit-msg.mjs";

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
