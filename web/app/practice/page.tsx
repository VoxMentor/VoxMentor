"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { api, ApiError, type QuestionDetail } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";
import QuestionCard from "@/components/QuestionCard";
import CodeEditor from "@/components/CodeEditor";

function PageSkeleton() {
  return (
    <div className="grid lg:grid-cols-[60%_40%] gap-6 min-w-0">
      <div className="card">
        <div className="h-6 w-48 bg-border rounded animate-pulse mb-4" />
        <div className="h-12 w-full bg-border rounded animate-pulse mb-3" />
        <div className="h-3 w-3/4 bg-border rounded-full animate-pulse" />
        <div className="h-40 w-full bg-border rounded-xl animate-pulse mt-4" />
      </div>
      <div className="card">
        <div className="h-10 w-32 bg-border rounded animate-pulse mb-4" />
        <div className="h-full bg-border rounded-xl animate-pulse" />
      </div>
    </div>
  );
}

interface PracticeContentProps {
  selectedId?: string;
}

function PracticeContent({ selectedId }: PracticeContentProps) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const [detailReload, setDetailReload] = useState(0);

  const detailKey = `${selectedId ?? ""}|${detailReload}`;
  const [detailState, setDetailState] = useState<{
    key: string;
    status: "ok" | "notFound" | "error";
    detail?: QuestionDetail;
    message?: string;
  } | null>(null);

  const detailLoading = !!selectedId && detailState?.key !== detailKey;
  const detailOk = detailState?.key === detailKey && detailState.status === "ok";
  const detail = detailOk ? (detailState.detail ?? null) : null;
  const detailNotFound = detailState?.key === detailKey && detailState.status === "notFound";
  const detailError =
    detailState?.key === detailKey && detailState.status === "error"
      ? (detailState.message ?? "Failed to load question.")
      : null;

  const updateParams = useCallback(
    (updates: Record<string, string | undefined>, replace = false) => {
      const params = new URLSearchParams(searchParams.toString());
      for (const [key, value] of Object.entries(updates)) {
        if (value === undefined) params.delete(key);
        else params.set(key, value);
      }
      const qs = params.toString();
      const url = `${pathname}${qs ? `?${qs}` : ""}`;
      if (replace) router.replace(url);
      else router.push(url);
    },
    [router, pathname, searchParams]
  );

  useEffect(() => {
    if (!selectedId) return;
    let cancelled = false;
    api
      .question(selectedId)
      .then((q) => {
        if (cancelled) return;
        setDetailState({ key: detailKey, status: "ok", detail: q });
      })
      .catch((e) => {
        if (cancelled) return;
        const notFound = e instanceof ApiError && e.status === 404;
        setDetailState({
          key: detailKey,
          status: notFound ? "notFound" : "error",
          message: notFound ? undefined : e instanceof ApiError ? e.message : "Failed to load question.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [selectedId, detailKey]);

  const retryDetail = () => setDetailReload((n) => n + 1);

  return (
    <div className="grid lg:grid-cols-[60%_40%] gap-6 min-w-0">
      {/* Left: question (60%) */}
      <section className="flex flex-col gap-4 min-w-0">
        {detailLoading && !detail ? (
          <div className="card h-full flex flex-col">
            <div className="h-6 w-48 bg-border rounded animate-pulse mb-4 flex-shrink-0" />
            <div className="h-12 w-full bg-border rounded animate-pulse mb-3 flex-shrink-0" />
            <div className="h-3 w-3/4 bg-border rounded-full animate-pulse flex-shrink-0" />
            <div className="flex-1 h-40 w-full bg-border rounded-xl animate-pulse" />
          </div>
        ) : detailNotFound ? (
          <div className="card text-center py-10 flex-1">
            <p className="text-sm text-text-muted mb-4">
              This question is no longer available.
            </p>
            <button
              onClick={() => updateParams({ question: undefined })}
              className="text-sm font-medium text-primary hover:underline cursor-pointer"
            >
              Pick another question
            </button>
          </div>
        ) : detailError ? (
          <FetchError message={detailError} onRetry={retryDetail} />
        ) : detail ? (
          <QuestionCard detail={detail} />
        ) : null}
      </section>

      {/* Right: code editor placeholder (40%) */}
      <section className="flex flex-col gap-4 min-w-0">
        {detail ? (
          <CodeEditor language="javascript" />
        ) : (
          <div className="card h-full flex flex-col items-center justify-center text-text-muted">
            <p className="text-sm">Select a question to open the editor.</p>
          </div>
        )}
      </section>
    </div>
  );
}

function PracticeRoute() {
  const searchParams = useSearchParams();
  const questionId = searchParams.get("question") ?? undefined;

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="max-w-6xl mx-auto px-6 py-10">
          <div className="mb-8">
            <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
              Practice
            </h1>
            <p className="text-text-muted mt-2 max-w-2xl">
              Work through questions and write your solution in the editor.
            </p>
          </div>

          <Suspense fallback={<PageSkeleton />}>
            <PracticeContent selectedId={questionId} />
          </Suspense>
        </main>
      </div>
    </ProtectedRoute>
  );
}

export default function PracticePage() {
  return (
    <Suspense fallback={<PageSkeleton />}>
      <PracticeRoute />
    </Suspense>
  );
}
