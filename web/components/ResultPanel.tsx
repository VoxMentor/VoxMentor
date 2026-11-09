"use client";

import { motion } from "motion/react";
import { type SubmitCodeResult } from "@/lib/api";

interface ResultPanelProps {
  result: SubmitCodeResult;
}

function statusLabel(result: SubmitCodeResult, isCorrect: boolean | null): string {
  if (isCorrect !== null) {
    return isCorrect ? "Correct" : "Incorrect";
  }
  return result.status ?? "Ungraded";
}

function borderColor(isCorrect: boolean | null): string {
  if (isCorrect === true) return "border-success";
  if (isCorrect === false) return "border-danger";
  return "border-border";
}

function badgeColor(isCorrect: boolean | null): string {
  if (isCorrect === true) {
    return "bg-success/10 border border-success/20 text-success";
  }
  if (isCorrect === false) {
    return "bg-danger/10 border border-danger/20 text-danger";
  }
  return "bg-border/10 border border-border text-text-muted";
}

function masteryColor(masteryDelta: number): string {
  if (masteryDelta > 0) return "var(--success, #10B981)";
  if (masteryDelta < 0) return "var(--danger, #EF4444)";
  return "var(--muted, #6B7280)";
}

function optimalLabel(isOptimal: boolean): string {
  if (isOptimal === true) return "· optimal";
  if (isOptimal === false) return "· not optimal";
  return "· unavailable";
}

/**
 * Displays a single code-submission result: pass/fail badge, test cases,
 * execution time, memory usage, mastery change, and optional AI evaluation.
 * Renders a neutral ungraded state when `isCorrect` is null and an
 * unavailable indicator for missing timing/memory measurements.
 */
export default function ResultPanel({ result }: ResultPanelProps) {
  const isCorrect = result.isCorrect;
  const masteryDelta = result.masteryDelta;

  return (
    <div className="card animate-fade-in flex flex-col gap-3 border-l-4">
      <div className={`border-l-3 px-2.5 py-2 ${borderColor(isCorrect)}`}>
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-medium text-text-heading">Submission result</h3>
          <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${badgeColor(isCorrect)}`}>
            {statusLabel(result, isCorrect)}
          </span>
        </div>
      </div>

      <div className="px-4 py-3 grid grid-cols-2 gap-2 border-b border-border">
        <div className="flex flex-col gap-1">
          <span className="text-[10px] font-medium text-text-muted uppercase tracking-wider">
            Test cases
          </span>
          <span className="text-sm font-medium text-text-body">
            {result.testCasesPassed} / {result.testCasesTotal}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-[10px] font-medium text-text-muted uppercase tracking-wider">
            Execution time
          </span>
          <span className="text-sm font-medium text-text-body">
            {result.executionTimeMs === null ||
            result.executionTimeMs === undefined
              ? "—"
              : `${result.executionTimeMs} ms`}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-[10px] font-medium text-text-muted uppercase tracking-wider">
            Memory usage
          </span>
          <span className="text-sm font-medium text-text-body">
            {result.memoryUsageKb === null ||
            result.memoryUsageKb === undefined
              ? "—"
              : `${result.memoryUsageKb} KB`}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-[10px] font-medium text-text-muted uppercase tracking-wider">
            Mastery change
          </span>
          <motion.span
            className="text-sm font-medium"
            initial={{ opacity: 0, y: -4 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.3 }}
            style={{ color: masteryColor(masteryDelta) }}
          >
            {masteryDelta === 0 ? "0.00" : `${masteryDelta >= 0 ? "+" : ""}${masteryDelta.toFixed(2)}`}
          </motion.span>
        </div>
      </div>

      {result.aiEvaluation && (
        <div className="px-4 py-3 border-b border-border">
          <h4 className="text-[10px] font-medium text-text-muted uppercase tracking-wider mb-2">
            AI evaluation
          </h4>
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-1">
              <span className="text-[10px] font-medium text-text-muted uppercase tracking-wider">
                Correctness
              </span>
              <span className="text-sm font-medium text-text-body">
                {result.aiEvaluation.correctness.score} /{" "}
                {result.aiEvaluation.correctness.maxScore}
              </span>
            </div>
            <div className="flex flex-col gap-1">
              <span className="text-[10px] font-medium text-text-muted uppercase tracking-wider">
                Code style
              </span>
              <span className="text-sm font-medium text-text-body">
                {result.aiEvaluation.codeStyle.score} /{" "}
                {result.aiEvaluation.codeStyle.maxScore}
              </span>
            </div>
            <div className="flex flex-col gap-1">
              <span className="text-[10px] font-medium text-text-muted uppercase tracking-wider">
                Time complexity
              </span>
              <span className="text-sm font-medium text-text-body">
                {result.aiEvaluation.timeComplexity.bigO}{" "}
                {optimalLabel(result.aiEvaluation.timeComplexity.isOptimal)}
              </span>
            </div>
            <div className="flex flex-col gap-1">
              <span className="text-[10px] font-medium text-text-muted uppercase tracking-wider">
                Space complexity
              </span>
              <span className="text-sm font-medium text-text-body">
                {result.aiEvaluation.spaceComplexity.bigO}{" "}
                {optimalLabel(result.aiEvaluation.spaceComplexity.isOptimal)}
              </span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
