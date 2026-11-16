"use client";

import type { MockInterviewReview } from "@/lib/api";

/**
 * The rubric review of a finished mock interview: overall score, per-problem
 * verdicts, readiness movement, and next steps. Shared by the live interview
 * and by a past interview loaded from history.
 */
export default function InterviewReviewCard({
  review,
}: {
  review: MockInterviewReview;
}) {
  const problems = Object.entries(review.technicalReview ?? {});
  const delta = review.readinessDelta;

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-end gap-x-10 gap-y-4">
        <div>
          <p className="text-xs text-text-muted">Score</p>
          <p className="mt-1 font-heading text-4xl font-semibold leading-none tabular-nums text-navy">
            {Math.round(review.score)}
            <span className="ml-1 font-mono text-sm font-normal text-text-muted">
              /100
            </span>
          </p>
        </div>
        {delta && (
          <div>
            <p className="text-xs text-text-muted">Readiness</p>
            <p className="mt-1 font-mono text-sm tabular-nums text-navy">
              {Math.round(delta.before)} to {Math.round(delta.after)}
              <span
                className={`ml-2 ${
                  delta.delta >= 0 ? "text-[#15803d]" : "text-[#b91c1c]"
                }`}
              >
                {delta.delta >= 0 ? "+" : ""}
                {Math.round(delta.delta)}
              </span>
            </p>
          </div>
        )}
      </div>

      <div className="border-t border-border pt-4">
        <h3 className="text-sm text-navy">
          {/* A Behavioral run is not a "Technical review". */}
          {review.type ? `${review.type} review` : "Technical review"}
        </h3>
        {problems.length === 0 ? (
          <p className="mt-2 text-sm text-text-muted">
            No per-problem feedback was recorded for this interview.
          </p>
        ) : (
          <ul className="mt-3 flex flex-col gap-2">
            {problems.map(([problemId, problem], index) => (
              <li key={problemId} className="rounded-[10px] border border-border p-3">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <span className="font-mono text-xs text-text-muted">
                    P{String(index + 1).padStart(2, "0")}
                  </span>
                  <span className="text-sm text-navy">
                    {problem.solved ? "Solved" : "Not solved"}
                  </span>
                  {problem.complexity && (
                    <span className="font-mono text-xs text-text-muted">
                      {problem.complexity}
                    </span>
                  )}
                </div>
                {problem.feedback && (
                  <p className="mt-1.5 break-words text-sm leading-relaxed text-text-body">
                    {problem.feedback}
                  </p>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      {review.nextSteps && (
        <div className="border-t border-border pt-4">
          <h3 className="text-sm text-navy">Next steps</h3>
          <p className="mt-1.5 break-words text-sm leading-relaxed text-text-body">
            {review.nextSteps}
          </p>
        </div>
      )}
    </div>
  );
}
