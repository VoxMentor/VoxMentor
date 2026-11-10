/**
  * Pure presentational helpers for the submission result panel.
  * Kept free of React/DOM dependencies so they can be unit-tested in isolation.
  */

import { type SubmitCodeResult } from "./api.ts";

/** Known execution-failure status prefixes (case-insensitive). */
const FAILURE_STATUS_PREFIXES = ["error", "runtime", "timeout", "crash", "fail", "internal"] as const;

/**
  * Returns the pass/fail verdict label: "Correct" or "Incorrect" when graded,
  * otherwise the submission status or "Ungraded". When a submission failed to
  * execute (timeout, crash, server error), the API's status takes precedence
  * over the generic "Incorrect" badge.
  */
export function statusLabel(result: SubmitCodeResult, isCorrect: boolean | null): string {
  if (isCorrect === true) return "Correct";
  if (isCorrect === false) {
    const status = result.status?.toLowerCase() ?? "";
    const isFailure = FAILURE_STATUS_PREFIXES.some((prefix) => status.startsWith(prefix));
    return isFailure ? (result.status ?? "Incorrect") : "Incorrect";
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
  if (!Number.isFinite(memoryUsageKb) || memoryUsageKb < 0) return "—";
  const mb = memoryUsageKb / 1024;
  if (mb.toFixed(2) === "0.00") {
    return `${memoryUsageKb} KB`;
  }
  return `${mb.toFixed(2)} MB`;
}

/**
 * Formats the mastery delta so small nonzero changes remain visible.
 * Zero or non-finite values are shown as "0.00" / "—", meaningful changes
 * keep two-decimal precision, and sub-0.005 changes show enough decimals to
 * stay distinguishable from zero.
 */
export function formatMasteryDelta(masteryDelta: number): string {
  if (!Number.isFinite(masteryDelta)) return "—";
  if (masteryDelta === 0) return "0.00";
  const rounded = masteryDelta.toFixed(2);
  // Check if rounded value is effectively zero (handles "-0.00" for negative small numbers)
  if (Number(rounded) === 0) {
    return `${masteryDelta >= 0 ? "+" : ""}${masteryDelta.toFixed(4)}`;
  }
  return `${masteryDelta >= 0 ? "+" : ""}${rounded}`;
}
