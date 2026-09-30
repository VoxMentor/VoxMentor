"use client";

import { Suspense, useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import {
  api,
  ApiError,
  type MasteryConcept,
  type QuestionDetail,
  type StudentQuestion,
} from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";
import QuestionList from "@/components/QuestionList";
import QuestionNavigation from "@/components/QuestionNavigation";

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

interface Filters {
  conceptId?: string;
  difficulty?: number;
}

function PracticeContent({
  filters,
  selectedId,
}: {
  filters: Filters;
  selectedId?: string;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const [concepts, setConcepts] = useState<MasteryConcept[]>([]);

  const [listReload, setListReload] = useState(0);
  const [detailReload, setDetailReload] = useState(0);

  // keyed results: loading/error are derived (never setState synchronously in an effect)
  const listKey = `${filters.conceptId ?? ""}|${filters.difficulty ?? ""}|${listReload}`;
  const [listState, setListState] = useState<
    | {
        key: string;
        ok: boolean;
        questions: StudentQuestion[];
        totalCount: number;
        message?: string;
      }
    | null
  >(null);
  const detailKey = `${selectedId ?? ""}|${detailReload}`;
  const [detailState, setDetailState] = useState<
    | {
        key: string;
        status: "ok" | "notFound" | "error";
        detail?: QuestionDetail;
        message?: string;
      }
    | null
  >(null);

  const listLoading = listState?.key !== listKey;
  const listOk = listState?.key === listKey && listState.ok;
  const listError =
    listState?.key === listKey && !listState.ok ? (listState.message ?? "Failed to load questions.") : null;
  const questions = useMemo(
    () => (listOk && listState ? listState.questions : []),
    [listOk, listState]
  );
  const totalCount = useMemo(
    () => (listOk && listState ? listState.totalCount : 0),
    [listOk, listState]
  );

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

  // concept catalog for the filter dropdown; non-fatal if it fails (filter hides)
  useEffect(() => {
    let cancelled = false;
    api
      .mastery()
      .then((m) => {
        if (!cancelled) setConcepts(m.concepts);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

  // ponytail: single pageSize=100 fetch (backend max, seed = 100); pager if totalCount > 100
  useEffect(() => {
    let cancelled = false;
    api
      .questions({ conceptId: filters.conceptId, difficulty: filters.difficulty, pageSize: 100 })
      .then((res) => {
        if (cancelled) return;
        setListState({
          key: listKey,
          ok: true,
          questions: res.questions,
          totalCount: res.totalCount,
        });
      })
      .catch((e) => {
        if (cancelled) return;
        setListState({
          key: listKey,
          ok: false,
          questions: [],
          totalCount: 0,
          message: e instanceof ApiError ? e.message : "Failed to load questions.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [listKey, filters.conceptId, filters.difficulty]);

  // auto-select first question when nothing valid is selected
  useEffect(() => {
    if (listLoading || questions.length === 0) return;
    if (!selectedId || !questions.some((q) => q.id === selectedId)) {
      updateParams({ question: questions[0].id }, true);
    }
  }, [listLoading, questions, selectedId, updateParams]);

  useEffect(() => {
    if (!selectedId) return;
    let cancelled = false;
    api
      .question(selectedId)
      .then((q) => {
        if (cancelled) return;
        setDetailState({ key: `${selectedId}|${detailReload}`, status: "ok", detail: q });
      })
      .catch((e) => {
        if (cancelled) return;
        const notFound = e instanceof ApiError && e.status === 404;
        setDetailState({
          key: `${selectedId}|${detailReload}`,
          status: notFound ? "notFound" : "error",
          message: notFound ? undefined : e instanceof ApiError ? e.message : "Failed to load question.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [selectedId, detailReload]);

  const index = useMemo(
    () => questions.findIndex((q) => q.id === selectedId),
    [questions, selectedId]
  );

  const selectAt = useCallback(
    (nextIndex: number) => {
      const next = questions[nextIndex];
      if (next) updateParams({ question: next.id });
    },
    [questions, updateParams]
  );

  const goPrev = useCallback(() => selectAt(index - 1), [selectAt, index]);
  const goNext = useCallback(() => selectAt(index + 1), [selectAt, index]);

  // keyboard prev/next — skip when typing in a form control
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement | null;
      if (!el) return;
      const tag = el.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable) return;
      if (e.key === "ArrowLeft" && index > 0) goPrev();
      else if (e.key === "ArrowRight" && index >= 0 && index < questions.length - 1) goNext();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [goPrev, goNext, index, questions.length]);

  const applyFilter = useCallback(
    (updates: { conceptId?: string; difficulty?: number }) => {
      updateParams({
        // read + write must share one key: `concept` (MasteryHeatmap deep links use it)
        concept: updates.conceptId,
        difficulty:
          updates.difficulty != null ? String(updates.difficulty) : undefined,
        question: undefined, // refetch picks a fresh first question
      });
    },
    [updateParams]
  );

  const clearFilters = useCallback(() => {
    updateParams({ concept: undefined, difficulty: undefined, question: undefined });
  }, [updateParams]);

  const retryList = () => setListReload((n) => n + 1);
  const retryDetail = () => setDetailReload((n) => n + 1);

  const position = index >= 0 ? index + 1 : 0;

  return (
    <div className="grid gap-6 lg:grid-cols-[18rem_1fr] items-start">
      <aside className="flex flex-col gap-3 lg:sticky lg:top-24">
        <div className="card !p-4 flex flex-col gap-3">
          <div>
            <label
              htmlFor="concept-filter"
              className="block text-xs font-medium text-text-muted mb-1"
            >
              Concept
            </label>
            <select
              id="concept-filter"
              value={filters.conceptId ?? ""}
              onChange={(e) => applyFilter({ conceptId: e.target.value || undefined, difficulty: filters.difficulty })}
              disabled={concepts.length === 0}
              className="w-full text-sm rounded-[14px] border-[1.5px] border-border bg-white px-4 py-3 text-text-body focus:border-primary focus:shadow-[0_0_0_3px_rgba(74,173,219,0.12)] focus:outline-none cursor-pointer"
            >
              <option value="">All concepts</option>
              {concepts.map((c) => (
                <option key={c.conceptId} value={c.conceptId}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label
              htmlFor="difficulty-filter"
              className="block text-xs font-medium text-text-muted mb-1"
            >
              Difficulty
            </label>
            <select
              id="difficulty-filter"
              value={filters.difficulty ?? ""}
              onChange={(e) =>
                applyFilter({
                  conceptId: filters.conceptId,
                  difficulty: e.target.value ? Number(e.target.value) : undefined,
                })
              }
              className="w-full text-sm rounded-[14px] border-[1.5px] border-border bg-white px-4 py-3 text-text-body focus:border-primary focus:shadow-[0_0_0_3px_rgba(74,173,219,0.12)] focus:outline-none cursor-pointer"
            >
              <option value="">All difficulties</option>
              {Array.from({ length: 10 }, (_, i) => i + 1).map((d) => (
                <option key={d} value={d}>
                  Difficulty {d}/10
                </option>
              ))}
            </select>
          </div>
        </div>

        {listLoading ? (
          <div className="card !p-4 flex flex-col gap-2">
            {Array.from({ length: 5 }, (_, i) => (
              <div key={i} className="h-10 bg-border rounded-xl animate-pulse" />
            ))}
          </div>
        ) : listError ? (
          <FetchError message={listError} onRetry={retryList} />
        ) : (
          <QuestionList
            questions={questions}
            selectedId={selectedId}
            onSelect={(id) => updateParams({ question: id })}
            emptyMessage={
              filters.conceptId || filters.difficulty != null
                ? "No questions match these filters."
                : "No questions available yet."
            }
            onClearFilters={
              filters.conceptId || filters.difficulty != null
                ? clearFilters
                : undefined
            }
          />
        )}
      </aside>

      <section className="flex flex-col gap-4 min-w-0">
        {detailLoading && !detail ? (
          <PracticeSkeleton />
        ) : detailNotFound ? (
          <div className="card text-center py-10">
            <p className="text-sm text-text-muted mb-4">
              This question is no longer available. Pick another from the list.
            </p>
            <Link
              href="/dashboard"
              className="text-sm font-medium text-primary hover:underline"
            >
              Back to dashboard
            </Link>
          </div>
        ) : detailError ? (
          <FetchError message={detailError} onRetry={retryDetail} />
        ) : detail ? (
          <div className="card">
            <div className="flex flex-wrap items-center gap-2 mb-3">
              <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-accent-light-blue text-primary">
                {detail.conceptName}
              </span>
              <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-bg-light text-text-muted">
                Difficulty {detail.difficulty}/10
              </span>
              <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-bg-light text-text-muted">
                {detail.questionType}
              </span>
            </div>

            <h1 className="font-heading font-semibold text-navy text-xl mb-3">
              {detail.title}
            </h1>
            <p className="text-sm text-text-body leading-relaxed whitespace-pre-line mb-6">
              {detail.description}
            </p>

            {detail.exampleInputs.length > 0 && (
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6">
                {detail.exampleInputs.map((input, i) => (
                  <div key={i} className="bg-navy-deep rounded-xl p-4">
                    <p className="text-xs text-accent-light-blue mb-1">
                      Example input {i + 1}
                    </p>
                    <pre className="text-sm text-white whitespace-pre-wrap">{input}</pre>
                    <p className="text-xs text-accent-light-blue mt-3 mb-1">
                      Expected output
                    </p>
                    <pre className="text-sm text-white whitespace-pre-wrap">
                      {detail.exampleOutputs[i] ?? ""}
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
        ) : null}

        {!listLoading && !listError && questions.length > 0 && index >= 0 && (
          <div className="card !py-4">
            <QuestionNavigation
              onPrev={goPrev}
              onNext={goNext}
              hasPrev={index > 0}
              hasNext={index >= 0 && index < questions.length - 1}
              position={position}
              total={totalCount}
            />
          </div>
        )}
      </section>
    </div>
  );
}

function PracticeRoute() {
  const searchParams = useSearchParams();
  const conceptId = searchParams.get("concept") ?? undefined;
  const difficultyRaw = searchParams.get("difficulty");
  const parsedDifficulty = difficultyRaw ? Number(difficultyRaw) : undefined;
  // guard: Number("abc")/Number("1.5") would send NaN/fraction → backend 400 loop
  const difficulty = Number.isInteger(parsedDifficulty) ? parsedDifficulty : undefined;
  const questionId = searchParams.get("question") ?? undefined;

  const filters = useMemo(
    () => ({ conceptId, difficulty }),
    [conceptId, difficulty]
  );

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="max-w-6xl mx-auto px-6 py-10">
          <PracticeContent filters={filters} selectedId={questionId} />
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
