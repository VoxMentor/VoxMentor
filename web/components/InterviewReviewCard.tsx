"use client";

import type { MockInterviewReview } from "@/lib/api";

/**
 * The rubric review of a finished mock interview: overall score, the
 * per-problem verdicts, the readiness movement it caused, and the model's
 * next steps. Shared by the live interview (right after completing) and by
 * the history list (a past interview's stored review).
 */
export default function InterviewReviewCard({
  review,
}: {
  review: MockInterviewReview;
}) {
  const problems = Object.entries(review.technicalReview ?? {});
  const delta = review.readinessDelta;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end gap-x-10 gap-y-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-text-muted">
            Score
          </p>
          <p className="font-heading text-4xl font-bold text-navy leading-none mt-1">
            {Math.round(review.score)}
            <span className="text-lg text-text-muted font-medium">/100</span>
          </p>
        </div>
        {delta && (
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-text-muted">
              Readiness
            </p>
            <p className="font-heading text-2xl font-bold text-navy leading-none mt-1">
              {Math.round(delta.before)}
              <span className="text-text-muted font-medium"> → </span>
              {Math.round(delta.after)}
              <span
                className={`text-sm font-semibold ml-2 ${
                  delta.delta >= 0 ? "text-success" : "text-danger"
                }`}
              >
                {delta.delta >= 0 ? "+" : ""}
                {Math.round(delta.delta)}
              </span>
            </p>
          </div>
        )}
      </div>

      <div>
        <h3 className="text-sm font-semibold text-text-heading mb-3">
          {/* Labelled from the review type: a Behavioral run is not a
              "Technical review" (CodeRabbit #176). */}
          {review.type ? `${review.type} review` : "Technical review"}
        </h3>
        {problems.length === 0 ? (
          <p className="text-sm text-text-muted">
            No per-problem feedback was recorded for this interview.
          </p>
        ) : (
          <ul className="space-y-3">
            {problems.map(([problemId, problem], index) => (
              <li key={problemId} className="rounded-xl border border-border p-4">
                <div className="flex flex-wrap items-center gap-2">
                  {/* Which problem this verdict belongs to - the feedback
                      alone is ambiguous when several were reviewed. */}
                  <span className="text-xs font-semibold text-text-heading">
                    Problem {index + 1}
                  </span>
                  <span
                    className={`text-xs font-semibold px-2 py-0.5 rounded-full ${
                      problem.solved
                        ? "bg-green-50 text-green-700"
                        : "bg-amber-50 text-amber-700"
                    }`}
                  >
                    {problem.solved ? "Solved" : "Not solved"}
                  </span>
                  {problem.complexity && (
                    <span className="text-xs text-text-muted">
                      {problem.complexity}
                    </span>
                  )}
                </div>
                {problem.feedback && (
                  <p className="text-sm text-text-body mt-2 break-words">
                    {problem.feedback}
                  </p>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      {review.nextSteps && (
        <div>
          <h3 className="text-sm font-semibold text-text-heading mb-2">
            Next steps
          </h3>
          <p className="text-sm text-text-body break-words">{review.nextSteps}</p>
        </div>
      )}
    </div>
  );
}
