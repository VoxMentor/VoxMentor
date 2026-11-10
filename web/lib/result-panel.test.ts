// Run with: npm test  (Node's built-in test runner - no test dependencies).
// Covers the pure presentational helpers for the submission result panel:
// formatMemory (KB/MB edge cases), optimalLabel (empty Big-O handling),
// statusLabel (Correct / Incorrect / execution-failure / Ungraded),
// borderColor, badgeColor, masteryColor, formatMasteryDelta.
import { test } from "node:test";
import assert from "node:assert/strict";
import { type SubmitCodeResult } from "./api.ts";
import {
  formatMemory,
  optimalLabel,
  statusLabel,
  borderColor,
  badgeColor,
  masteryColor,
  formatMasteryDelta,
} from "./result-panel.ts";

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

test("borderColor returns success for true", () => {
  assert.equal(borderColor(true), "border-success");
});

test("borderColor returns danger for false", () => {
  assert.equal(borderColor(false), "border-danger");
});

test("borderColor returns border for null", () => {
  assert.equal(borderColor(null), "border-border");
});

test("badgeColor returns success classes for true", () => {
  assert.equal(badgeColor(true), "bg-success/10 border border-success/20 text-success");
});

test("badgeColor returns danger classes for false", () => {
  assert.equal(badgeColor(false), "bg-danger/10 border border-danger/20 text-danger");
});

test("badgeColor returns muted classes for null", () => {
  assert.equal(badgeColor(null), "bg-border/10 border border-border text-text-muted");
});

test("masteryColor returns success for positive delta", () => {
  assert.equal(masteryColor(0.5), "var(--success, #10B981)");
});

test("masteryColor returns danger for negative delta", () => {
  assert.equal(masteryColor(-0.3), "var(--danger, #EF4444)");
});

test("masteryColor returns muted for zero delta", () => {
  assert.equal(masteryColor(0), "var(--muted, #6B7280)");
});

test("formatMasteryDelta handles non-finite values", () => {
  assert.equal(formatMasteryDelta(NaN), "—");
  assert.equal(formatMasteryDelta(Infinity), "—");
  assert.equal(formatMasteryDelta(-Infinity), "—");
});

test("formatMasteryDelta handles zero", () => {
  assert.equal(formatMasteryDelta(0), "0.00");
  assert.equal(formatMasteryDelta(-0), "0.00");
});

test("formatMasteryDelta formats positive deltas with sign", () => {
  assert.equal(formatMasteryDelta(0.5), "+0.50");
  assert.equal(formatMasteryDelta(1.234), "+1.23");
});

test("formatMasteryDelta formats negative deltas", () => {
  assert.equal(formatMasteryDelta(-0.5), "-0.50");
  assert.equal(formatMasteryDelta(-1.234), "-1.23");
});

test("formatMasteryDelta uses 4 decimals for small values", () => {
  assert.equal(formatMasteryDelta(0.001), "+0.0010");
  assert.equal(formatMasteryDelta(-0.001), "-0.0010");
  assert.equal(formatMasteryDelta(0.0049), "+0.0049");
});
