"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { api, ApiError, type NextQuestion } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";

function PracticeSkeleton() {
  return (
    <div className="card">
      <div className="h-6 w-64 bg-border rounded animate-pulse mb-4" />
      <div className="h-4 w-full bg-border rounded animate-pulse mb-2" />
      <div className="h-4 w-3/4 bg-border rounded animate-pulse mb-6" />
      <div className="h-40 w-full bg-border rounded-xl animate-pulse" />
    </div>
  );
}

function PracticeContent({ conceptId }: { conceptId?: string }) {
  const [question, setQuestion] = useState<NextQuestion | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    api
      .nextQuestion(conceptId)
      .then(setQuestion)
      .catch((e) => {
        if (e instanceof ApiError && e.status === 404) setNotFound(true);
        else setError(e instanceof ApiError ? e.message : "Failed to load question.");
      })
      .finally(() => setLoading(false));
  }, [conceptId]);

  const retry = () => {
    setLoading(true);
    setError(null);
    setNotFound(false);
    load();
  };

  useEffect(() => {
    load();
  }, [load]);

  if (loading) return <PracticeSkeleton />;

  if (notFound) {
    return (
      <div className="card text-center py-10">
        <p className="text-sm text-text-muted mb-4">
          No questions available here yet. Try another concept.
        </p>
        <Link
          href="/dashboard"
          className="text-sm font-medium text-primary hover:underline"
        >
          Back to dashboard
        </Link>
      </div>
    );
  }

  if (error) return <FetchError message={error} onRetry={retry} />;
  if (!question) return null;

  return (
    <div className="card">
      <div className="flex flex-wrap items-center gap-2 mb-3">
        <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-accent-light-blue text-primary">
          {question.conceptName}
        </span>
        <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-bg-light text-text-muted">
          Difficulty {question.difficulty}/10
        </span>
        <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-bg-light text-text-muted">
          {question.questionType}
        </span>
      </div>

      <h1 className="font-heading font-semibold text-navy text-xl mb-3">
        {question.title}
      </h1>
      <p className="text-sm text-text-body leading-relaxed whitespace-pre-line mb-6">
        {question.description}
      </p>

      {question.exampleInputs.length > 0 && (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6">
          {question.exampleInputs.map((input, i) => (
            <div key={i} className="bg-navy-deep rounded-xl p-4">
              <p className="text-xs text-accent-light-blue mb-1">Example input {i + 1}</p>
              <pre className="text-sm text-white whitespace-pre-wrap">{input}</pre>
              <p className="text-xs text-accent-light-blue mt-3 mb-1">Expected output</p>
              <pre className="text-sm text-white whitespace-pre-wrap">
                {question.exampleOutputs[i] ?? ""}
              </pre>
            </div>
          ))}
        </div>
      )}

      <div className="flex items-center justify-end">
        {/* ponytail: no submit flow here yet — questions only advance after a submission exists */}
        <Link
          href="/dashboard"
          className="px-4 py-2 text-sm font-medium text-white bg-primary hover:bg-primary-dark rounded-xl transition-colors"
        >
          Back to dashboard
        </Link>
      </div>
    </div>
  );
}

function PracticeRoute() {
  const conceptId = useSearchParams().get("concept") ?? undefined;
  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="max-w-3xl mx-auto px-6 py-10">
          {/* key remounts on concept change: clears stale notFound/error and
              ignores responses still in flight for the previous concept */}
          <PracticeContent key={conceptId ?? "default"} conceptId={conceptId} />
        </main>
      </div>
    </ProtectedRoute>
  );
}

export default function PracticePage() {
  return (
    <Suspense fallback={<PracticeSkeleton />}>
      <PracticeRoute />
    </Suspense>
  );
}
