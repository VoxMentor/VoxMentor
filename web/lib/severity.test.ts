// Run with: npm test  (Node's built-in test runner - no test dependencies).
// Covers the shared severity-to-colour helper used by the readiness and JD
// gap lists: one dot colour per severity band.
import { test } from "node:test";
import assert from "node:assert/strict";
import { severityColor } from "./severity.ts";

test("severityColor is red at or above the high band", () => {
  assert.equal(severityColor(0.5), "bg-red-500");
  assert.equal(severityColor(0.9), "bg-red-500");
  assert.equal(severityColor(1), "bg-red-500");
});

test("severityColor is yellow in the middle band", () => {
  assert.equal(severityColor(0.25), "bg-yellow-400");
  assert.equal(severityColor(0.49), "bg-yellow-400");
});

test("severityColor is green below the middle band", () => {
  assert.equal(severityColor(0), "bg-green-500");
  assert.equal(severityColor(0.24), "bg-green-500");
});
