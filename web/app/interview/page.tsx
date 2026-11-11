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
        <main className="max-w-6xl mx-auto px-6 py-10">
          <div className="mb-8">
            <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
              Mock interview
            </h1>
            <p className="text-text-muted mt-2 max-w-2xl">
              Practise against an AI interviewer. Answers stream in live and you
              get a scored review at the end.
            </p>
          </div>

          <div className="grid lg:grid-cols-[minmax(0,1fr)_320px] gap-6 items-start">
            <div className="flex flex-col gap-6 min-w-0">
              <InterviewChat
                initialJdId={jdId}
                onCompleted={refreshHistory}
                resumeRequest={resumeRequest}
              />

              {pastError && (
                <div className="error-banner !mb-0" role="alert">
                  <span aria-hidden="true">⚠️</span>
                  <span>{pastError}</span>
                </div>
              )}

              {pastReview && (
                <div className="card !p-5">
                  <h2 className="font-heading font-semibold text-navy text-lg mb-4">
                    Review from{" "}
                    {new Date(pastReview.completedAt ?? pastReview.startedAt)
                      .toLocaleDateString(undefined, {
                        year: "numeric",
                        month: "short",
                        day: "numeric",
                      })}
                  </h2>
                  <InterviewReviewCard review={pastReview.review} />
                </div>
              )}
            </div>

            <aside className="flex flex-col gap-3 min-w-0">
              <h2 className="font-heading font-semibold text-navy text-base">
                Interview history
              </h2>
              <InterviewHistory
                refreshKey={historyKey}
                selectedId={selectedId}
                onSelect={openHistoryItem}
              />
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
