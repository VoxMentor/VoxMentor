// Run with: npm test  (Node's built-in test runner - no test dependencies).
// Covers the pure presentational helpers for the submission result panel:
// formatMemory (KB/MB edge cases), optimalLabel (empty Big-O handling),
// and statusLabel (Correct / Incorrect / execution-failure / Ungraded).
import { test } from "node:test";
import assert from "node:assert/strict";
import { type SubmitCodeResult } from "./api.ts";
import { formatMemory, optimalLabel, statusLabel } from "./result-panel.ts";

function makeResult(overrides: Partial<SubmitCodeResult>): SubmitCodeResult {
  return {
    submissionId: "test",
    status: "success",
    isCorrect: null,
    testCasesPassed: 0,
    testCasesTotal: 0,
    executionTimeMs: null,
    memoryUsageKb: null,
    aiEvaluation: null,
    testCaseResults: [],
    previousMastery: 0,
    newMastery: 0,
    masteryDelta: 0,
    ...overrides,
  };
}

test("formatMemory renders missing values as an unavailable indicator", () => {
  assert.equal(formatMemory(null), "—");
  assert.equal(formatMemory(undefined), "—");
});

test("formatMemory treats negative memory as unavailable", () => {
  assert.equal(formatMemory(-1), "—");
  assert.equal(formatMemory(-100), "—");
});

test("formatMemory keeps small positive measurements in KB", () => {
  // 1 KB -> 0.00 MB rounds to 0.00, so show KB instead.
  assert.equal(formatMemory(1), "1 KB");
  // 511 KB -> 0.50 MB, shown in MB.
  assert.equal(formatMemory(511), "0.50 MB");
});

test("formatMemory converts larger values to MB with two decimals", () => {
  assert.equal(formatMemory(1024), "1.00 MB");
  assert.equal(formatMemory(2048), "2.00 MB");
  assert.equal(formatMemory(1536), "1.50 MB");
  assert.equal(formatMemory(1048576), "1024.00 MB");
});

test("formatMemory rounds MB to two decimal places", () => {
  assert.equal(formatMemory(1234), "1.21 MB"); // 1234/1024 = 1.205...
});

test("optimalLabel treats empty Big-O as unavailable", () => {
  assert.equal(optimalLabel("", true), "· unavailable");
  assert.equal(optimalLabel("", false), "· unavailable");
  assert.equal(optimalLabel("", null), "· unavailable");
});

test("optimalLabel treats missing assessment as unavailable", () => {
  assert.equal(optimalLabel("O(n)", null), "· unavailable");
});

test("optimalLabel renders explicit optimality distinctly", () => {
  assert.equal(optimalLabel("O(n)", true), "· optimal");
  assert.equal(optimalLabel("O(n)", false), "· not optimal");
});

test("formatMemory treats NaN as unavailable", () => {
  assert.equal(formatMemory(NaN), "—");
});

test("formatMemory treats Infinity as unavailable", () => {
  assert.equal(formatMemory(Infinity), "—");
});

test("formatMemory treats negative Infinity as unavailable", () => {
  assert.equal(formatMemory(-Infinity), "—");
});

test("statusLabel returns Correct when isCorrect is true", () => {
  assert.equal(statusLabel(makeResult({ status: "success", isCorrect: true }), true), "Correct");
});

test("statusLabel returns Incorrect for false without error status", () => {
  assert.equal(statusLabel(makeResult({ status: "success", isCorrect: false }), false), "Incorrect");
});

test("statusLabel returns execution-failure status when isCorrect is false", () => {
  assert.equal(statusLabel(makeResult({ status: "timeout", isCorrect: false }), false), "timeout");
  assert.equal(statusLabel(makeResult({ status: "CRASHED", isCorrect: false }), false), "CRASHED");
  assert.equal(
    statusLabel(makeResult({ status: "Internal Server Error", isCorrect: false }), false),
    "Internal Server Error",
  );
});

test("statusLabel returns Ungraded when isCorrect is null", () => {
  assert.equal(statusLabel(makeResult({ status: "pending" }), null), "pending");
  assert.equal(statusLabel(makeResult({ status: undefined }), null), "Ungraded");
});
