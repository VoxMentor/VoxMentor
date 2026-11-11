// Run with: npm test  (Node's built-in test runner - no test dependencies).
// Covers the mock-interview transcript helpers (#129): streamed tokens land in
// the turn being filled, and the opening falls back to the start result's
// message when nothing streamed.
import { test } from "node:test";
import assert from "node:assert/strict";
import { appendToken, openingTurns, type InterviewTurn } from "./interview.ts";

test("appendToken adds the token to the turn being filled", () => {
  const turns: InterviewTurn[] = [
    { id: 1, role: "interviewer", text: "Hel" },
    { id: 2, role: "candidate", text: "Hi" },
  ];

  const next = appendToken(turns, 1, "lo");

  assert.equal(next[0].text, "Hello");
  assert.equal(next[1].text, "Hi");
  assert.equal(next.length, 2);
});

test("appendToken drops a token for an unknown turn", () => {
  const turns: InterviewTurn[] = [{ id: 1, role: "interviewer", text: "Hello" }];

  const next = appendToken(turns, 99, "stale");

  assert.equal(next, turns);
});

test("openingTurns keeps the streamed opening", () => {
  const turns: InterviewTurn[] = [{ id: 1, role: "interviewer", text: "Hi" }];

  const next = openingTurns(turns, 2, "fallback message");

  assert.equal(next, turns);
});

test("openingTurns falls back to the opening message when nothing streamed", () => {
  const turns: InterviewTurn[] = [{ id: 1, role: "interviewer", text: "" }];

  const next = openingTurns(turns, 2, "fallback message");

  assert.equal(next.length, 1);
  assert.equal(next[0].role, "interviewer");
  assert.equal(next[0].text, "fallback message");
});
