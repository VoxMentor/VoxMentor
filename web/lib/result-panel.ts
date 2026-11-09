/**
 * Pure presentational helpers for the submission result panel.
 * Kept free of React/DOM dependencies so they can be unit-tested in isolation.
 */

import { type SubmitCodeResult } from "./api.ts";

/**
 * Returns the pass/fail verdict label: "Correct" or "Incorrect" when graded,
 * otherwise the submission status or "Ungraded".
 */
export function statusLabel(result: SubmitCodeResult, isCorrect: boolean | null): string {
  if (isCorrect !== null) {
    return isCorrect ? "Correct" : "Incorrect";
  }
  return result.status ?? "Ungraded";
}

/**
 * Returns the left border color: success/danger when graded, neutral otherwise.
 */
export function borderColor(isCorrect: boolean | null): string {
  if (isCorrect === true) return "border-success";
  if (isCorrect === false) return "border-danger";
  return "border-border";
}

/**
 * Returns the pill badge color: green/red when graded, neutral otherwise.
 */
export function badgeColor(isCorrect: boolean | null): string {
  if (isCorrect === true) {
    return "bg-success/10 border border-success/20 text-success";
  }
  if (isCorrect === false) {
    return "bg-danger/10 border border-danger/20 text-danger";
  }
  return "bg-border/10 border border-border text-text-muted";
}

/**
 * Returns the mastery delta color: green when improved, red when regressed,
 * muted when flat.
 */
export function masteryColor(masteryDelta: number): string {
  if (masteryDelta > 0) return "var(--success, #10B981)";
  if (masteryDelta < 0) return "var(--danger, #EF4444)";
  return "var(--muted, #6B7280)";
}

/**
 * Returns the complexity optimality assessment alongside its Big-O value.
 * An empty Big-O or missing assessment renders as unavailable; an explicit
 * false renders as not optimal.
 */
export function optimalLabel(bigO: string, isOptimal: boolean | null): string {
  if (!bigO) return "· unavailable";
  if (isOptimal === true) return "· optimal";
  if (isOptimal === false) return "· not optimal";
  return "· unavailable";
}

/**
 * Formats a memory usage value in kilobytes for display.
 * Converts to megabytes (MB) with two decimals, but keeps small positive
 * measurements in KB when MB would round to 0.00. Missing or negative values
 * show "—".
 */
export function formatMemory(memoryUsageKb: number | null | undefined): string {
  if (memoryUsageKb === null || memoryUsageKb === undefined) {
    return "—";
  }
  if (memoryUsageKb < 0) return "—";
  const mb = memoryUsageKb / 1024;
  if (mb.toFixed(2) === "0.00") {
    return `${memoryUsageKb} KB`;
  }
  return `${mb.toFixed(2)} MB`;
}
