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
import MasteryProgressBar from "@/components/MasteryProgressBar";
import ConceptCard from "@/components/ConceptCard";

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

  const [detailReload, setDetailReload] = useState(0);
  const [listReload, setListReload] = useState(0);
  const [concepts, setConcepts] = useState<MasteryConcept[]>([]);
  const [overallReadiness, setOverallReadiness] = useState(0);
  const [conceptsLoading, setConceptsLoading] = useState(true);
  const [conceptsError, setConceptsError] = useState<string | null>(null);

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

  // --- Editor state (per-question, per-language drafts) ---
  const [language, setLanguage] = useState<string>("javascript");
  // Key: `${questionId}|${language}` — keeps drafts isolated per question.
  const [codeDrafts, setCodeDrafts] = useState<Record<string, string>>({});
  const draftKey = selectedId ? `${selectedId}|${language}` : "";
  const code = draftKey ? codeDrafts[draftKey] ?? LANGUAGE_TEMPLATES[language] ?? "" : "";

  const [submitting, setSubmitting] = useState(false);
  const [submitResult, setSubmitResult] = useState<{
    questionId: string;
    result: SubmitCodeResult;
  } | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // Identity of the in-flight submission. A monotonically increasing counter
  // ensures we only surface results for the most recent submission; edits or
  // language switches increment the counter, invalidating prior in-flight requests.
  const submitIdentity = useRef(0);

  // Separate sequence counter for the submitting flag — allows clearing it in
  // finally only when the request still owns the latest submission, even when
  // submitIdentity is nullified by edits/language changes.
  const submitSeq = useRef(0);

  // Refs for aborting in-flight submission on unmount and clearing its timeout.
  const submitController = useRef<AbortController | null>(null);
  const submitTimeoutId = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Ref for the deferred mastery timer so it can be cleared on cleanup.
  const masteryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Controller for cancelling in-flight mastery fetches.
  const masteryController = useRef<AbortController | null>(null);

  // Mounted ref to prevent setState after unmount.
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // Sanitize server error messages before surfacing to the client UI.
  function sanitizeError(err: unknown): string {
    if (err instanceof ApiError) {
      return "Request failed. Please try again.";
    }
    return "Something went wrong. Please try again.";
  }

  // Invalidate the in-flight submission: increment identity, abort request, clear submitting.
  const invalidateSubmission = useCallback(() => {
    submitIdentity.current++;
    submitSeq.current++;
    submitController.current?.abort();
    setSubmitting(false);
  }, []);

  // Reset editor state when the question changes, scoped to that question.
  const currentQuestionIdRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    if (currentQuestionIdRef.current === selectedId) return;
    currentQuestionIdRef.current = selectedId;
    setLanguage("javascript");
    setCodeDrafts({});
    setSubmitResult(null);
    setSubmitError(null);
    // Invalidate any in-flight submission so it cannot surface a result or
    // error on the newly navigated question.
    invalidateSubmission();
  }, [selectedId, invalidateSubmission]);

  const updateCodeDraft = (newValue: string) => {
    if (!draftKey) return;
    setCodeDrafts((prev) => ({ ...prev, [draftKey]: newValue }));
  };

  const handleLanguageChange = (newLanguage: string) => {
    setLanguage(newLanguage);
    // Restore the selected language's draft (falling back to its template),
    // never leaking code from the previously selected language.
    setSubmitResult(null);
    setSubmitError(null);
    invalidateSubmission();
  };

  // Request ID to guard against stale mastery responses
  const masteryRequestId = useRef(0);

  // Fetch mastery data (concepts + overall readiness). Cancellable via AbortController
  // so that stale requests don't overwrite fresher state and can be aborted on unmount.
  const fetchMastery = useCallback(async () => {
    // Cancel any in-flight mastery request before starting a new one.
    masteryController.current?.abort();
    const controller = new AbortController();
    masteryController.current = controller;

    // Assign a new request ID; only the latest request may update state.
    const requestId = ++masteryRequestId.current;

    setConceptsLoading(true);
    setConceptsError(null);
    try {
      const m = await api.mastery({ signal: controller.signal });
      // Only apply if this is still the latest request and component is mounted.
      if (mountedRef.current && requestId === masteryRequestId.current) {
        setConcepts(m.concepts);
        setOverallReadiness(m.overallReadiness);
      }
    } catch (err) {
      if (err instanceof DOMException && err.name === "AbortError") return;
      // Only update error if this is still the latest request and mounted.
      if (mountedRef.current && requestId === masteryRequestId.current) {
        setConceptsError("Failed to load mastery data. Please try again.");
      }
    } finally {
      if (mountedRef.current && requestId === masteryRequestId.current) {
        setConceptsLoading(false);
      }
    }
  }, []);

  const handleSubmit = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    if (!detail?.id || submitting) return;

    const codeToSubmit = code;
    const languageToSubmit = language;
    const submissionId = ++submitIdentity.current;
    // Separate sequence for the submitting flag — allows clearing it in
    // finally only when this request still owns the latest submission.
    const seq = ++submitSeq.current;
    setSubmitting(true);
    setSubmitResult(null);
    setSubmitError(null);

    // Abort any in-flight submission before starting a new one.
    submitController.current?.abort();
    const controller = new AbortController();
    submitController.current = controller;

    // Abort after 60s to prevent frozen UI if backend hangs.
    const timeoutId = setTimeout(() => controller.abort(), 60_000);
    submitTimeoutId.current = timeoutId;

    try {
      const result = await api.submitCode(
        { questionId: detail.id, code: codeToSubmit, language: languageToSubmit },
        { signal: controller.signal },
      );
      // Only surface the result if this is still the latest submission.
      if (submitIdentity.current === submissionId) {
        setSubmitResult({ questionId: detail.id, result });
        // Refresh mastery so the readiness bar and concept cards reflect
        // the new values after a successful submission.
        // Fire-and-forget: mastery errors should not mask successful submission.
        fetchMastery();
      }
    } catch (err) {
      if (err instanceof DOMException && err.name === "AbortError") {
        if (submitIdentity.current === submissionId) {
          setSubmitError("Request timed out. Please try again.");
        }
      } else {
        // Only surface the error if this is still the latest submission.
        if (submitIdentity.current === submissionId) {
          setSubmitError(sanitizeError(err));
        }
      }
    } finally {
      clearTimeout(timeoutId);
      if (submitTimeoutId.current === timeoutId) {
        submitTimeoutId.current = null;
      }
      if (submitController.current === controller) {
        submitController.current = null;
      }
      // Only clear submitting if this is still the latest submission.
      if (submitSeq.current === seq) {
        setSubmitting(false);
      }
    }
  }, [detail, code, language, submitting, fetchMastery]);

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
          message: notFound ? undefined : e instanceof ApiError ? sanitizeError(e) : "Failed to load question.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [selectedId, detailKey]);

  const retryDetail = () => setDetailReload((n) => n + 1);

  // --- Question list state ---
  const filters = useMemo(
    () => {
      const rawDifficulty = searchParams.get("difficulty");
      let difficulty: number | undefined;
      if (rawDifficulty !== null && rawDifficulty !== "") {
        const v = Number(rawDifficulty);
        if (Number.isInteger(v) && v >= 1 && v <= 10) {
          difficulty = v;
        }
      }
      return {
        conceptId: searchParams.get("concept") ?? undefined,
        difficulty,
      };
    },
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
    // Defer to avoid synchronous setState in effect body.
    if (masteryTimerRef.current !== null) clearTimeout(masteryTimerRef.current);
    masteryTimerRef.current = setTimeout(() => {
      masteryTimerRef.current = null;
      fetchMastery();
    }, 0);
    return () => {
      if (masteryTimerRef.current !== null) {
        clearTimeout(masteryTimerRef.current);
        masteryTimerRef.current = null;
      }
    };
  }, [fetchMastery]);

  // Unmount cleanup: abort in-flight submission and clear its timeout.
  useEffect(() => {
    return () => {
      submitController.current?.abort();
      masteryController.current?.abort();
      if (masteryTimerRef.current !== null) {
        clearTimeout(masteryTimerRef.current);
        masteryTimerRef.current = null;
      }
      if (submitTimeoutId.current !== null) {
        clearTimeout(submitTimeoutId.current);
        submitTimeoutId.current = null;
      }
    };
  }, []);

  // Fetch question list with AbortController for cancellation on filter change/unmount.
  useEffect(() => {
    const controller = new AbortController();
    // Capture listKey at effect start to guard against stale responses.
    const myListKey = listKey;
    api
      .questions({
        conceptId: filters.conceptId,
        difficulty: filters.difficulty,
        pageSize: 100,
        signal: controller.signal,
      })
      .then((res) => {
        // Only apply if this request is still current.
        if (myListKey !== listKey) return;
        setListState({
          key: listKey,
          ok: true,
          questions: res.questions,
        });
      })
      .catch((e) => {
        if (e instanceof DOMException && e.name === "AbortError") return;
        // Only update error state if this request is still current.
        if (myListKey !== listKey) return;
        setListState({
          key: listKey,
          ok: false,
          questions: [],
          message: e instanceof ApiError ? sanitizeError(e) : "Failed to load questions.",
        });
      });
    return () => {
      controller.abort();
    };
  }, [listKey, filters.conceptId, filters.difficulty]);

  useEffect(() => {
    if (listLoading || questions.length === 0) return;
    if (!selectedId || !questions.some((q) => q.id === selectedId)) {
      updateParams({ question: questions[0].id }, true);
    }
  }, [listLoading, questions, selectedId, updateParams, listKey]);

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
        <div className="card !p-4 flex flex-col gap-3">
          <div className="flex items-center justify-between mb-2">
            <h2 className="text-sm font-medium text-text-heading">
              Readiness
            </h2>
            <span className="text-sm font-medium text-navy">
              {Math.round(overallReadiness * 100)}%
            </span>
          </div>
          <MasteryProgressBar
            mastery={overallReadiness}
            label="Overall readiness"
          />
          <details>
            <summary className="cursor-pointer text-sm">Concept mastery</summary>
            <div className="grid gap-2">
              {concepts
                .slice()
                .sort(
                  (a, b) =>
                    (a.masteryProbability ?? 0) -
                    (b.masteryProbability ?? 0)
                )
                .map((c) => (
                  <ConceptCard key={c.conceptId} concept={c} />
                ))}
            </div>
          </details>
          {conceptsError && !conceptsLoading && (
            <div className="p-3 rounded-[12px] text-sm bg-red-50 border border-red-200 text-red-800 flex items-center justify-between">
              <span>{conceptsError}</span>
              <button
                onClick={fetchMastery}
                className="text-sm font-medium text-primary hover:underline cursor-pointer ml-4"
              >
                Retry
              </button>
            </div>
          )}
        </div>

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
                  disabled={conceptsLoading || concepts.length === 0}
                  className="w-full text-sm rounded-[14px] border-[1.5px] border-border bg-white px-4 py-3 text-text-body focus:border-primary focus:shadow-[0_0_0_3px_rgba(74,173,219,0.12)] focus:outline-none cursor-pointer"
                >
                  <option value="">All concepts</option>
                  {conceptsLoading ? (
                    <option disabled>Loading concepts…</option>
                  ) : (
                    concepts.map((c) => (
                      <option key={c.conceptId} value={c.conceptId}>
                        {c.name}
                      </option>
                    ))
                  )}
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
                invalidateSubmission();
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
  const rawQuestionId = searchParams.get("question") ?? undefined;
  // Validate questionId format (UUID or numeric ID) before using.
  const questionId = /^[0-9a-fA-F-]{8,}$/.test(rawQuestionId ?? "") ? rawQuestionId : undefined;

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
  return (
    <Suspense fallback={<PageSkeleton />}>
      <PracticeRoute />
    </Suspense>
  );
}

