"use client";

import { useEffect, useState } from "react";
import { api, ApiError, type QuestionDetail, type SubmissionItem } from "@/lib/api";

interface QuestionCardProps {
  detail: QuestionDetail;
  position: number;
}

/**
 * Problem pane: LeetCode-style numbered title, difficulty band, Description
 * and Submissions tabs. Examples stay inside the description as data rows.
 */
export default function QuestionCard({ detail, position }: QuestionCardProps) {
  const [tab, setTab] = useState<"description" | "submissions">("description");
  const [submissions, setSubmissions] = useState<SubmissionItem[] | null>(null);
  const [submissionsError, setSubmissionsError] = useState(false);

  const difficulty =
    detail.difficulty <= 3
      ? "Easy"
      : detail.difficulty <= 6
        ? "Medium"
        : "Hard";
  const difficultyClass =
    difficulty === "Easy"
      ? "bg-success/10 text-[#15803d]"
      : difficulty === "Medium"
        ? "bg-accent-gold/10 text-[#a16207]"
        : "bg-danger/10 text-[#b91c1c]";

  useEffect(() => {
    if (tab !== "submissions") return;
    let cancelled = false;
    api
      .submissions(20)
      .then((items) => {
        if (!cancelled) setSubmissions(items.filter((s) => s.questionTitle === detail.title));
      })
      .catch((e) => {
        if (!cancelled) setSubmissionsError(e instanceof ApiError);
      });
    return () => {
      cancelled = true;
    };
  }, [tab, detail.title]);

  return (
    <div className="panel flex min-w-0 flex-col gap-4">
      <div>
        <div className="flex flex-wrap items-center gap-2">
          <span
            className={`rounded-md px-2 py-0.5 text-xs font-medium ${difficultyClass}`}
          >
            {difficulty}
          </span>
          <span className="text-xs text-text-muted">{detail.conceptName}</span>
        </div>
        <h2 className="mt-3 font-heading text-xl font-semibold leading-tight text-navy">
          {String(position).padStart(2, "0")}. {detail.title}
        </h2>
      </div>

      <div className="flex gap-4 border-b border-border" role="tablist">
        {(["description", "submissions"] as const).map((value) => (
          <button
            key={value}
            type="button"
            role="tab"
            aria-selected={tab === value}
            onClick={() => setTab(value)}
            className={`-mb-px cursor-pointer border-b-2 px-1 pb-2 text-sm font-medium capitalize transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy ${
              tab === value
                ? "border-navy text-navy"
                : "border-transparent text-text-muted hover:text-navy"
            }`}
          >
            {value}
          </button>
        ))}
      </div>

      {tab === "description" ? (
        <div role="tabpanel" className="flex flex-col gap-5">
          <p className="whitespace-pre-line text-sm leading-relaxed text-text-body">
            {detail.description}
          </p>

          {detail.exampleInputs.map((input, i) => (
            <div key={i} className="rounded-[10px] bg-bg-light p-3">
              <p className="text-xs font-medium text-navy">Example {i + 1}</p>
              <dl className="mt-2 grid gap-1 font-mono text-xs leading-relaxed">
                <div className="flex gap-3">
                  <dt className="w-16 shrink-0 text-text-muted">Input</dt>
                  <dd className="min-w-0 whitespace-pre-wrap break-words text-text-body">
                    {input}
                  </dd>
                </div>
                <div className="flex gap-3">
                  <dt className="w-16 shrink-0 text-text-muted">Output</dt>
                  <dd className="min-w-0 whitespace-pre-wrap break-words text-text-body">
                    {detail.exampleOutputs[i] ?? "No expected output"}
                  </dd>
                </div>
              </dl>
            </div>
          ))}

          <div className="border-t border-border pt-4">
            <p className="text-xs font-medium text-text-muted">Test plan</p>
            <p className="mt-1 font-mono text-xs tabular-nums text-text-body">
              {detail.totalTestCases} {detail.totalTestCases === 1 ? "case" : "cases"},{" "}
              {detail.hiddenTestCaseCount} hidden
            </p>
          </div>
        </div>
      ) : (
        <div role="tabpanel">
          {submissionsError ? (
            <p className="text-sm text-text-body">Failed to load submissions.</p>
          ) : !submissions ? (
            <div className="flex flex-col gap-2">
              {Array.from({ length: 3 }, (_, i) => (
                <div key={i} className="h-10 rounded-md bg-border animate-pulse" />
              ))}
            </div>
          ) : submissions.length === 0 ? (
            <p className="text-sm text-text-muted">
              No submissions for this question yet.
            </p>
          ) : (
            <ul className="flex flex-col gap-2">
              {submissions.map((s) => (
                <li
                  key={s.submissionId}
                  className="flex items-center justify-between gap-3 rounded-[10px] border border-border px-3 py-2"
                >
                  <span className="flex items-center gap-2 text-sm text-navy">
                    <span
                      aria-hidden="true"
                      className={`h-1.5 w-1.5 rounded-full ${s.isCorrect ? "bg-success" : "bg-danger"}`}
                    />
                    {s.isCorrect ? "Accepted" : "Wrong answer"}
                  </span>
                  <span className="font-mono text-xs tabular-nums text-text-muted">
                    {new Date(s.createdAt).toLocaleDateString()}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
