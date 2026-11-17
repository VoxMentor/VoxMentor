"use client";

import { motion } from "motion/react";
import { type SubmitCodeResult, type CodeTestCaseResult } from "@/lib/api";
import {
  borderColor,
  formatMemory,
  formatMasteryDelta,
  masteryColor,
  optimalLabel,
  statusLabel,
} from "@/lib/result-panel";

interface ResultPanelProps {
  result: SubmitCodeResult;
}

// ponytail: the tested lib helpers use an em dash for "unavailable"; normalize
// it at the edge so no decorative dashes reach the screen.
function display(value: string): string {
  return value === "—" ? "n/a" : value;
}

/**
 * Displays a single code-submission result: pass/fail badge, test cases,
 * execution time, memory usage, mastery change, visible test-case results,
 * and optional AI evaluation.
 * Renders a neutral ungraded state when `isCorrect` is null and an
 * unavailable indicator for missing timing/memory measurements.
 */
export default function ResultPanel({ result }: ResultPanelProps) {
  const isCorrect = result.isCorrect;
  const masteryDelta = result.masteryDelta;
  const status = statusLabel(result, isCorrect);

  return (
    <div className="animate-fade-in relative flex flex-col gap-3 overflow-hidden rounded-[10px] border border-border">
      <div
        aria-hidden="true"
        className={`absolute left-0 top-0 h-full w-0.5 ${
          isCorrect === true
            ? "bg-success"
            : isCorrect === false
              ? "bg-danger"
              : "bg-border"
        }`}
      />
      <div className="flex items-center justify-between gap-3 px-3 pt-3">
        <h3 className="text-sm font-medium text-navy">Submission result</h3>
        <span className="flex shrink-0 items-center gap-2 text-xs font-medium text-navy">
          <span
            aria-hidden="true"
            className={`h-2 w-2 rounded-full border-2 ${borderColor(isCorrect)}`}
          />
          {status}
        </span>
      </div>

      <div className="px-4 py-3 grid grid-cols-2 gap-2 border-b border-border">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-text-muted">
            Test cases
          </span>
          <span className="text-sm font-medium text-text-body">
            {result.testCasesPassed} / {result.testCasesTotal}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-text-muted">
            Execution time
          </span>
          <span className="text-sm font-medium text-text-body">
            {result.executionTimeMs == null ? "n/a" : `${result.executionTimeMs} ms`}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-text-muted">
            Memory usage
          </span>
          <span className="text-sm font-medium text-text-body">
            {display(formatMemory(result.memoryUsageKb))}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-text-muted">
            Mastery change
          </span>
          <motion.span
            className="text-sm font-medium"
            initial={{ opacity: 0, y: -4 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.3 }}
            style={{ color: masteryColor(masteryDelta) }}
          >
            {display(formatMasteryDelta(masteryDelta))}
          </motion.span>
        </div>
      </div>

      {result.testCaseResults && result.testCaseResults.length > 0 && (
        <div className="px-4 py-3 border-b border-border">
          <h4 className="mb-2 text-xs text-text-muted">
            Test case results
          </h4>
          <div className="space-y-2 max-h-60 overflow-y-auto">
            {result.testCaseResults.map((tc: CodeTestCaseResult) => (
              <div
                key={tc.testCaseIndex}
                className={`p-2 rounded bg-white border ${
                  tc.passed ? "border-success/30" : "border-danger/30"
                }`}
              >
                <div className="flex items-center justify-between text-xs mb-1">
                  <span className="font-medium text-text-muted">Test case #{tc.testCaseIndex + 1}</span>
                  <span
                    className={`rounded px-1.5 py-0.5 text-[11px] font-medium text-navy ${
                      tc.passed ? "bg-success/10" : "bg-danger/10"
                    }`}
                  >
                    {tc.passed ? "Passed" : "Failed"}
                  </span>
                </div>
                <div className="grid grid-cols-2 gap-2 text-[11px] text-text-body">
                  <div>
                    <span className="text-text-muted">Input:</span>
                    <pre className="whitespace-pre-wrap font-mono mt-0.5">{tc.stdin || "n/a"}</pre>
                  </div>
                  <div>
                    <span className="text-text-muted">Expected:</span>
                    <pre className="whitespace-pre-wrap font-mono mt-0.5">{tc.expected || "n/a"}</pre>
                  </div>
                  {!tc.passed && (
                    <>
                      <div>
                        <span className="text-text-muted">Actual:</span>
                        <pre className="whitespace-pre-wrap font-mono mt-0.5">{tc.actual || "n/a"}</pre>
                      </div>
                      <div />
                    </>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {result.aiEvaluation && (
        <div className="px-4 py-3 border-b border-border">
          <h4 className="mb-2 text-xs text-text-muted">
            AI evaluation
          </h4>
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-1">
              <span className="text-xs text-text-muted">
                Correctness
              </span>
              <span className="text-sm font-medium text-text-body">
                {result.aiEvaluation.correctness.score ?? "n/a"} /{" "}
                {result.aiEvaluation.correctness.maxScore ?? "n/a"}
              </span>
              {result.aiEvaluation.correctness.feedback && (
                <p className="text-[11px] text-text-muted mt-1">
                  {result.aiEvaluation.correctness.feedback}
                </p>
              )}
            </div>
            <div className="flex flex-col gap-1">
              <span className="text-xs text-text-muted">
                Code style
              </span>
              <span className="text-sm font-medium text-text-body">
                {result.aiEvaluation.codeStyle.score ?? "n/a"} /{" "}
                {result.aiEvaluation.codeStyle.maxScore ?? "n/a"}
              </span>
              {result.aiEvaluation.codeStyle.feedback && (
                <p className="text-[11px] text-text-muted mt-1">
                  {result.aiEvaluation.codeStyle.feedback}
                </p>
              )}
            </div>
            <div className="flex flex-col gap-1">
              <span className="text-xs text-text-muted">
                Time complexity
              </span>
              <span className="text-sm font-medium text-text-body">
                {result.aiEvaluation.timeComplexity.bigO}{" "}
                {optimalLabel(
                  result.aiEvaluation.timeComplexity.bigO,
                  result.aiEvaluation.timeComplexity.isOptimal,
                )}
              </span>
              {result.aiEvaluation.timeComplexity.feedback && (
                <p className="text-[11px] text-text-muted mt-1">
                  {result.aiEvaluation.timeComplexity.feedback}
                </p>
              )}
            </div>
            <div className="flex flex-col gap-1">
              <span className="text-xs text-text-muted">
                Space complexity
              </span>
              <span className="text-sm font-medium text-text-body">
                {result.aiEvaluation.spaceComplexity.bigO}{" "}
                {optimalLabel(
                  result.aiEvaluation.spaceComplexity.bigO,
                  result.aiEvaluation.spaceComplexity.isOptimal,
                )}
              </span>
              {result.aiEvaluation.spaceComplexity.feedback && (
                <p className="text-[11px] text-text-muted mt-1">
                  {result.aiEvaluation.spaceComplexity.feedback}
                </p>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
