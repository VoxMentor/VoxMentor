"use client";

import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import {
  api,
  ApiError,
  type MasteryConcept,
  type QuestionDetail,
  type StudentQuestion,
  type SubmitCodeResult,
} from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";
import QuestionCard from "@/components/QuestionCard";
import CodeEditor from "@/components/CodeEditor";
import LanguageSelector from "@/components/LanguageSelector";
import QuestionList from "@/components/QuestionList";
import ResultPanel from "@/components/ResultPanel";

/**
 * Rendered while question data is loading. Mirrors the 60% question / 40%
 * editor layout so the page doesn't shift when content arrives.
 */
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

/**
 * Renders the practice page content: a selectable question list on the left
 * (60%) and the question detail + code editor on the right (40%). Fetches the
 * selected question on demand with loading, not-found, and error states.
 */
function PracticeContent({ selectedId }: PracticeContentProps) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  // Keep the latest searchParams in a ref so updateParams stays stable across
  // reads without being recreated on every searchParams object identity change.
  const searchParamsRef = useRef(searchParams);
  useEffect(() => {
    searchParamsRef.current = searchParams;
  }, [searchParams]);

  const [detailReload, setDetailReload] = useState(0);
  const [listReload, setListReload] = useState(0);
  const [conceptsReload, setConceptsReload] = useState(0);
  const [concepts, setConcepts] = useState<MasteryConcept[]>([]);

  // --- Question detail state ---
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

  const LANGUAGE_TEMPLATES: Record<string, string> = {
    python: "def solve():\n    # Write your solution here\n    pass\n",
    java: "public class Solution {\n    public static void main(String[] args) {\n        // Write your solution here\n    }\n}",
    cpp: '#include <iostream>\nusing namespace std;\n\nint main() {\n    // Write your solution here\n    return 0;\n}',
    javascript: "function solve() {\n    // Write your solution here\n}\n",
    csharp: "using System;\n\nclass Solution {\n    static void Main() {\n        // Write your solution here\n    }\n}",
    c: "#include <stdio.h>\n\nint main() {\n    // Write your solution here\n    return 0;\n}",
  };

  // --- Editor state (per-language drafts) ---
  const [language, setLanguage] = useState<string>("javascript");
  const [codeDrafts, setCodeDrafts] = useState<Record<string, string>>({});
  const code = codeDrafts[language] ?? LANGUAGE_TEMPLATES[language] ?? "";

  const [submitting, setSubmitting] = useState(false);
  const [submitResult, setSubmitResult] = useState<{
    questionId: string;
    result: SubmitCodeResult;
  } | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // Identity of the in-flight submission (question + language + code). A result
  // is only applied if this identity is still current when the request finishes,
  // so edits or language switches while a request is pending don't surface stale
  // results for a different submission.
  const submitIdentity = useRef<{
    questionId: string;
    language: string;
    code: string;
  } | null>(null);

  // Ownership of the submit loading state, tracked independently of result
  // identity (which edits can invalidate). Only the submission that currently
  // owns the loading state may clear it, so an earlier request's finally block
  // cannot re-enable the button while a later submission is still in flight.
  const submitOwner = useRef<{
    questionId: string;
    language: string;
    code: string;
  } | null>(null);

  // Reset editor state when the question changes, scoped to that question.
  const currentQuestionIdRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    if (currentQuestionIdRef.current === selectedId) return;
    currentQuestionIdRef.current = selectedId;
    setLanguage("javascript");
    setCodeDrafts({});
    setSubmitResult(null);
    setSubmitError(null);
    setSubmitting(false);
    // Invalidate any in-flight submission so it cannot surface a result or
    // error on the newly navigated question.
    submitIdentity.current = null;
  }, [selectedId]);

  const updateCodeDraft = (newValue: string) => {
    setCodeDrafts((prev) => ({ ...prev, [language]: newValue }));
  };

  const [conceptsError, setConceptsError] = useState<string | null>(null);

  const handleLanguageChange = (newLanguage: string) => {
    setLanguage(newLanguage);
    // Restore the selected language's draft (falling back to its template),
    // never leaking code from the previously selected language.
    setSubmitResult(null);
    setSubmitError(null);
    submitIdentity.current = null;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!detail?.id || submitting) return;

    const codeToSubmit = code;
    const identity = { questionId: detail.id, language, code: codeToSubmit };
    submitIdentity.current = identity;
    submitOwner.current = identity;
    setSubmitting(true);
    setSubmitResult(null);
    setSubmitError(null);
    try {
      const result = await api.submitCode({
        questionId: detail.id,
        code: codeToSubmit,
        language,
      });
      // Only surface the result if the submission identity is still current.
      if (submitIdentity.current === identity) {
        setSubmitResult({ questionId: detail.id, result });
      }
    } catch (err) {
      // Only surface the error if the submission identity is still current.
      if (submitIdentity.current === identity) {
        if (err instanceof ApiError) {
          setSubmitError(err.message);
        } else {
          setSubmitError("Something went wrong. Please try again.");
        }
      }
    } finally {
      // Only the submission that owns the loading state may clear it.
      if (submitOwner.current === identity) {
        setSubmitting(false);
      }
    }
  };

  const updateParams = useCallback(
    (updates: Record<string, string | undefined>, replace = false) => {
      const params = new URLSearchParams(searchParamsRef.current.toString());
      for (const [key, value] of Object.entries(updates)) {
        if (value === undefined) params.delete(key);
        else params.set(key, value);
      }
      const qs = params.toString();
      const url = `${pathname}${qs ? `?${qs}` : ""}`;
      if (replace) router.replace(url);
      else router.push(url);
    },
    [router, pathname]
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

  // --- Question list state ---
  const filters = useMemo(
    () => ({
      conceptId: searchParams.get("concept") ?? undefined,
      difficulty: searchParams.get("difficulty")
        ? Number(searchParams.get("difficulty"))
        : undefined,
    }),
    [searchParams]
  );

  const listKey = `${filters.conceptId ?? ""}|${filters.difficulty ?? ""}|${listReload}`;
  const [listState, setListState] = useState<{
    key: string;
    ok: boolean;
    questions: StudentQuestion[];
    message?: string;
  } | null>(null);

  const listLoading = listState?.key !== listKey;
  const listOk = listState?.key === listKey && listState.ok;
  const listError =
    listState?.key === listKey && !listState.ok
      ? (listState.message ?? "Failed to load questions.")
      : null;
  const questions = useMemo(
    () => (listOk && listState ? listState.questions : []),
    [listOk, listState]
  );

  useEffect(() => {
    let cancelled = false;
    api
      .mastery()
      .then((m) => {
        if (!cancelled) {
          setConcepts(m.concepts);
          setConceptsError(null);
        }
      })
      .catch((e) => {
        if (cancelled) return;
        setConcepts([]);
        setConceptsError(
          e instanceof ApiError ? e.message : "Failed to load concepts.",
        );
      });
    return () => {
      cancelled = true;
    };
  }, [conceptsReload]);

  useEffect(() => {
    let cancelled = false;
    api
      .questions({
        conceptId: filters.conceptId,
        difficulty: filters.difficulty,
        pageSize: 100,
      })
      .then((res) => {
        if (cancelled) return;
        setListState({
          key: listKey,
          ok: true,
          questions: res.questions,
        });
      })
      .catch((e) => {
        if (cancelled) return;
        setListState({
          key: listKey,
          ok: false,
          questions: [],
          message: e instanceof ApiError ? e.message : "Failed to load questions.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [listKey, filters.conceptId, filters.difficulty]);

  useEffect(() => {
    if (listLoading || questions.length === 0) return;
    if (!selectedId || !questions.some((q) => q.id === selectedId)) {
      updateParams({ question: questions[0].id }, true);
    }
  }, [listLoading, questions, selectedId, updateParams]);

  const applyFilter = useCallback(
    (updates: { conceptId?: string; difficulty?: number }) => {
      updateParams({
        concept: updates.conceptId,
        difficulty:
          updates.difficulty != null ? String(updates.difficulty) : undefined,
        question: undefined,
      });
    },
    [updateParams]
  );

  const clearFilters = useCallback(() => {
    updateParams({ concept: undefined, difficulty: undefined, question: undefined });
  }, [updateParams]);

  const retryList = () => setListReload((n) => n + 1);

  return (
    <div className="grid lg:grid-cols-[60%_40%] gap-6 min-w-0">
      {/* Left: question (60%) */}
      <section className="flex flex-col gap-4 min-w-0">
        {detail ? (
          <QuestionCard detail={detail} />
        ) : (
          <>
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
                  onChange={(e) =>
                    applyFilter({
                      conceptId: e.target.value || undefined,
                      difficulty: filters.difficulty,
                    })}
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
                    })}
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
            ) : conceptsError ? (
              <FetchError
                message={conceptsError}
                onRetry={() => setConceptsReload((n) => n + 1)}
              />
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
          </>
        )}
      </section>

      {/* Right: code editor + language selector + submit (40%) */}
      <section className="flex flex-col gap-4 min-w-0 h-full">
        {detailLoading && !detail ? (
          <div className="card h-full flex flex-col">
            <div className="h-10 w-32 bg-border rounded animate-pulse mb-4" />
            <div className="h-full bg-border rounded-xl animate-pulse" />
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
          <form onSubmit={handleSubmit} className="flex flex-col gap-3 h-full">
            <div>
              <label
                htmlFor="language-selector"
                className="block text-xs font-medium text-text-muted mb-1"
              >
                Language
              </label>
              <LanguageSelector value={language} onChange={handleLanguageChange} />
            </div>
            <CodeEditor
              language={language}
              value={code}
              onChange={(newValue) => {
                updateCodeDraft(newValue ?? "");
                setSubmitResult(null);
                setSubmitError(null);
                // Invalidate any in-flight submission: an edit makes its
                // identity stale, so its result must not surface later.
                submitIdentity.current = null;
              }}
              height="100%"
            />
            {detail?.id &&
              submitResult?.questionId === detail.id && (
                <ResultPanel result={submitResult.result} />
              )}
            {submitError && (
              <div
                className="p-3 rounded-[12px] text-sm bg-red-50 border border-red-200 text-red-800"
              >
                {submitError}
              </div>
            )}
            <button
              type="submit"
              disabled={submitting || !detail?.id}
              className="btn-primary"
            >
              {submitting ? (
                <span className="flex items-center justify-center gap-2">
                  <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24" fill="none">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                  </svg>
                  Submitting...
                </span>
              ) : (
                "Submit Code"
              )}
            </button>
          </form>
        ) : (
          <div className="card h-full flex flex-col items-center justify-center text-text-muted">
            <p className="text-sm">Select a question to open the editor.</p>
          </div>
        )}
      </section>
    </div>
  );
}

/**
 * Practice page route. Reads the question query parameter, guards the route
 * with ProtectedRoute, and renders PracticeContent with a Navbar and Suspense
 * boundary at /practice.
 */
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

/**
 * Top-level practice page route. Wraps the practice content in ProtectedRoute,
 * Navbar, and a Suspense boundary, then renders at /practice.
 */
export default function PracticePage() {
  return <PracticeRoute />;
}

