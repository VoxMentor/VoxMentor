"use client";

import { Suspense, useCallback, useRef, useState } from "react";
import { useSearchParams } from "next/navigation";
import {
  api,
  ApiError,
  type MockInterviewHistoryItem,
  type MockInterviewReviewDetail,
} from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import SkeletonLoader from "@/components/SkeletonLoader";
import InterviewChat from "@/components/InterviewChat";
import InterviewHistory from "@/components/InterviewHistory";
import InterviewReviewCard from "@/components/InterviewReviewCard";

function InterviewRoute() {
  const searchParams = useSearchParams();
  // /interview?jd=<id> — pre-fills the job description from the JD page.
  const jdId = searchParams.get("jd") ?? undefined;

  const [historyKey, setHistoryKey] = useState(0);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  // A new object per InProgress click: InterviewChat re-runs its resume
  // effect on identity, so clicking a different unfinished row always lands.
  const [resumeRequest, setResumeRequest] = useState<{ id: string } | null>(
    null
  );
  const [pastReview, setPastReview] = useState<MockInterviewReviewDetail | null>(
    null
  );
  const [pastError, setPastError] = useState<string | null>(null);
  // Bumped per selection so a slow review response for a row the user has
  // already clicked away from can never overwrite the newer one (CodeRabbit #176).
  const reviewSeq = useRef(0);

  const refreshHistory = useCallback(
    () => setHistoryKey((k) => k + 1),
    []
  );

  /**
   * Opens a history row: an unfinished one is handed to the chat to resume
   * (otherwise the daily quota could strand the student with no way back in),
   * a finished one loads its stored review.
   */
  const openHistoryItem = useCallback(
    async (item: MockInterviewHistoryItem) => {
      const seq = ++reviewSeq.current;
      setSelectedId(item.interviewId);
      setPastReview(null);
      setPastError(null);
      if (item.status === "InProgress") {
        setResumeRequest({ id: item.interviewId });
        return;
      }
      if (item.status !== "Completed") {
        setPastError("That interview has not been completed yet.");
        return;
      }
      try {
        const detail = await api.mockReview(item.interviewId);
        if (seq !== reviewSeq.current) return; // a newer selection won
        setPastReview(detail);
      } catch (e) {
        if (seq !== reviewSeq.current) return;
        setPastError(
          e instanceof ApiError && e.status === 404
            ? "No review is stored for that interview yet."
            : "Could not load that review."
        );
      }
    },
    []
  );

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="mx-auto w-full max-w-[1400px] px-8 py-10">
          <header className="mb-8">
            <h1 className="font-heading text-2xl font-semibold text-navy">
              Mock interview
            </h1>
            <p className="mt-1 text-sm text-text-muted">
              Answer the interviewer, then review your score and feedback.
            </p>
          </header>

          <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,1fr)_320px]">
            <div className="flex min-w-0 flex-col gap-5">
              <InterviewChat
                initialJdId={jdId}
                onCompleted={refreshHistory}
                resumeRequest={resumeRequest}
              />

              {pastError && (
                <p role="alert" className="error-banner !mb-0">
                  {pastError}
                </p>
              )}

              {pastReview && (
                <section className="panel">
                  <div className="flex flex-wrap items-baseline justify-between gap-3">
                    <h2 className="text-sm font-medium text-navy">Past review</h2>
                    <span className="font-mono text-xs text-text-muted">
                      {new Date(pastReview.completedAt ?? pastReview.startedAt)
                        .toLocaleDateString(undefined, {
                          year: "numeric",
                          month: "short",
                          day: "numeric",
                        })}
                    </span>
                  </div>
                  <div className="mt-4">
                    <InterviewReviewCard review={pastReview.review} />
                  </div>
                </section>
              )}
            </div>

            <aside className="flex min-w-0 flex-col gap-5">
              <div className="panel">
                <h2 className="text-sm font-medium text-navy">History</h2>
                <div className="mt-4">
                  <InterviewHistory
                    refreshKey={historyKey}
                    selectedId={selectedId}
                    onSelect={openHistoryItem}
                  />
                </div>
              </div>

              <div className="panel">
                <h2 className="text-sm font-medium text-navy">How it works</h2>
              <ol className="mt-4 flex flex-col gap-4">
                {[
                  [
                    "01",
                    "Read the question",
                    "The interviewer streams each question into the transcript.",
                  ],
                  [
                    "02",
                    "Send your answer",
                    "Your reply is recorded and the interviewer responds.",
                  ],
                  [
                    "03",
                    "Finish for a review",
                    "Get a score, per-problem feedback, and next steps.",
                  ],
                ].map(([n, title, description]) => (
                  <li key={n} className="flex gap-3">
                    <span className="font-mono text-xs tabular-nums text-text-muted">
                      {n}
                    </span>
                    <div className="min-w-0">
                      <p className="text-sm text-navy">{title}</p>
                      <p className="mt-0.5 text-xs leading-relaxed text-text-muted">
                        {description}
                      </p>
                    </div>
                  </li>
                ))}
              </ol>
              </div>
            </aside>
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}

export default function InterviewPage() {
  return (
    <Suspense fallback={<SkeletonLoader />}>
      <InterviewRoute />
    </Suspense>
  );
}
