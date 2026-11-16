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
import { formatMemory, statusLabel } from "@/lib/result-panel";

/** Sanitize server error messages before surfacing to the client UI. */
function sanitizeError(err: unknown): string {
  if (err instanceof ApiError) {
    return "Request failed. Please try again.";
  }
  return "Something went wrong. Please try again.";
}

// ponytail: the tested result helpers use an em dash for unavailable values.
// Normalize it at the edge so screen text stays in one plain register.
function show(value: string): string {
  return value === "—" ? "n/a" : value;
}

function PageSkeleton() {
  return (
    <div className="flex min-w-0 flex-col gap-5">
      <div className="flex items-end justify-between border-b border-border pb-5">
        <div className="h-8 w-32 rounded bg-border animate-pulse" />
        <div className="h-9 w-48 rounded-[10px] bg-border animate-pulse" />
      </div>
      <div className="grid min-w-0 grid-cols-1 gap-5 lg:grid-cols-2">
        <div className="panel flex flex-col gap-4">
          <div className="h-4 w-32 rounded bg-border animate-pulse" />
          <div className="h-7 w-3/4 rounded bg-border animate-pulse" />
          <div className="h-4 w-full rounded bg-border animate-pulse" />
          <div className="h-32 rounded-[10px] bg-border animate-pulse" />
        </div>
        <div className="panel flex flex-col gap-4">
          <div className="h-5 w-28 rounded bg-border animate-pulse" />
          <div className="min-h-[18rem] rounded-[10px] bg-border animate-pulse" />
          <div className="h-9 rounded-[10px] bg-border animate-pulse" />
        </div>
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

  const FILE_EXTENSIONS: Record<string, string> = {
    python: "py",
    java: "java",
    cpp: "cpp",
    javascript: "js",
    csharp: "cs",
    c: "c",
  };

  // --- Editor state (per-question, per-language drafts) ---
  const [language, setLanguage] = useState<string>("javascript");
  // Key: `${questionId}|${language}` keeps drafts isolated per question.
  const [codeDrafts, setCodeDrafts] = useState<Record<string, string>>({});
  const draftKey = selectedId ? `${selectedId}|${language}` : "";
  const code = draftKey ? codeDrafts[draftKey] ?? LANGUAGE_TEMPLATES[language] ?? "" : "";

  const [submitting, setSubmitting] = useState(false);
  const [submitResult, setSubmitResult] = useState<{
    questionId: string;
    result: SubmitCodeResult;
  } | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [editorTab, setEditorTab] = useState("testcase-0");
  const [navigatorOpen, setNavigatorOpen] = useState(false);

  // Identity of the in-flight submission. A monotonically increasing counter
  // ensures we only surface results for the most recent submission; edits or
  // language switches increment the counter, invalidating prior in-flight requests.
  const submitIdentity = useRef(0);

  // Refs for aborting in-flight submission on unmount and clearing its timeout.
  const submitController = useRef<AbortController | null>(null);
  const submitTimeoutId = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Ref for the deferred mastery timer so it can be cleared on cleanup.
  const masteryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Controller for cancelling in-flight mastery fetches.
  const masteryController = useRef<AbortController | null>(null);

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
    setEditorTab("testcase-0");
    // Invalidate any in-flight submission so it cannot surface a result or
    // error on the newly navigated question.
    submitIdentity.current++;
  }, [selectedId]);

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
    submitIdentity.current++;
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
      // Only apply if this is still the latest request.
      if (requestId === masteryRequestId.current) {
        setConcepts(m.concepts);
        setOverallReadiness(m.overallReadiness);
      }
    } catch (err) {
      if (err instanceof DOMException && err.name === "AbortError") return;
      // Only update error if this is still the latest request.
      if (requestId === masteryRequestId.current) {
        setConceptsError("Failed to load mastery data. Please try again.");
      }
    } finally {
      if (requestId === masteryRequestId.current) {
        setConceptsLoading(false);
      }
    }
  }, []);

  const handleSubmit = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    if (!detail?.id || submitting) return;

    const codeToSubmit = code;
    const submissionId = ++submitIdentity.current;
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
        { questionId: detail.id, code: codeToSubmit, language },
        { signal: controller.signal },
      );
      // Only surface the result if this is still the latest submission.
      if (submitIdentity.current === submissionId) {
        setSubmitResult({ questionId: detail.id, result });
        setEditorTab("result");
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
      submitTimeoutId.current = null;
      submitController.current = null;
      // Only clear submitting if this is still the latest submission.
      if (submitIdentity.current === submissionId) {
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
      if (submitTimeoutId.current !== null) {
        clearTimeout(submitTimeoutId.current);
        submitTimeoutId.current = null;
      }
    };
  }, []);

  // Fetch question list with AbortController for cancellation on filter change/unmount.
  useEffect(() => {
    const controller = new AbortController();
    api
      .questions({
        conceptId: filters.conceptId,
        difficulty: filters.difficulty,
        pageSize: 100,
        signal: controller.signal,
      })
      .then((res) => {
        setListState({
          key: listKey,
          ok: true,
          questions: res.questions,
        });
      })
      .catch((e) => {
        if (e instanceof DOMException && e.name === "AbortError") return;
        setListState({
          key: listKey,
          ok: false,
          questions: [],
          message: e instanceof ApiError ? e.message : "Failed to load questions.",
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

  // ponytail: derived from the same state the submit button uses, so the
  // readout can never disagree with the submission it describes.
  const runStatus = submitting
    ? { dot: "bg-navy-light", text: "Running tests" }
    : submitError
      ? { dot: "bg-danger", text: "Submission failed" }
      : submitResult && detail && submitResult.questionId === detail.id
        ? {
            dot:
              submitResult.result.isCorrect === true
                ? "bg-success"
                : submitResult.result.isCorrect === false
                  ? "bg-danger"
                  : "bg-navy-light",
            text: `${submitResult.result.testCasesPassed} of ${submitResult.result.testCasesTotal} tests passed`,
          }
        : { dot: "bg-border", text: "Ready to run" };

  const selectedIndex = questions.findIndex((q) => q.id === selectedId);
  const position = selectedIndex >= 0 ? selectedIndex + 1 : 0;
  const pad = (n: number) => String(n).padStart(2, "0");
  const goToQuestion = (offset: number) => {
    const target = questions[selectedIndex + offset];
    if (target) updateParams({ question: target.id });
  };

  return (
    <div className="flex min-w-0 flex-col gap-5">
      <header className="flex flex-wrap items-end justify-between gap-4 border-b border-border pb-5">
        <div>
          <h1 className="font-heading text-2xl font-semibold text-navy">Practice</h1>
          <p className="mt-1 text-sm text-text-muted">
            Solve a question, then run it against its tests.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            onClick={() => setNavigatorOpen((open) => !open)}
            aria-expanded={navigatorOpen}
            className="cursor-pointer rounded-[10px] border border-border bg-bg-card px-3 py-2 text-sm font-medium text-text-body transition-colors hover:bg-bg-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy"
          >
            Questions
          </button>
          <div className="text-right">
            <p className="text-xs font-medium text-text-muted">Question</p>
            <p className="mt-0.5 font-mono text-sm tabular-nums text-navy">
              {position ? `${pad(position)} / ${pad(questions.length)}` : "-- / --"}
            </p>
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => goToQuestion(-1)}
              disabled={selectedIndex <= 0}
              className="cursor-pointer rounded-[10px] border border-border bg-bg-card px-3 py-2 text-sm font-medium text-text-body transition-colors hover:bg-bg-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy disabled:cursor-not-allowed disabled:opacity-50"
            >
              Previous
            </button>
            <button
              type="button"
              onClick={() => goToQuestion(1)}
              disabled={selectedIndex < 0 || selectedIndex >= questions.length - 1}
              className="cursor-pointer rounded-[10px] border border-border bg-bg-card px-3 py-2 text-sm font-medium text-text-body transition-colors hover:bg-bg-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy disabled:cursor-not-allowed disabled:opacity-50"
            >
              Next
            </button>
          </div>
        </div>
      </header>

      {/* Question navigator, opened from the header. All values come from the API. */}
      {navigatorOpen && (
        <section className="panel-sm grid min-w-0 grid-cols-1 gap-4 lg:grid-cols-[320px_minmax(0,1fr)]">
          <div className="flex flex-col gap-4">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-1">
              <div>
                <label
                  htmlFor="concept-filter"
                  className="mb-1 block text-xs font-medium text-text-muted"
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
                  className="field"
                >
                  <option value="">All concepts</option>
                  {conceptsLoading ? (
                    <option disabled>Loading concepts</option>
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
                  className="mb-1 block text-xs font-medium text-text-muted"
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
                  className="field"
                >
                  <option value="">All difficulties</option>
                  {Array.from({ length: 10 }, (_, i) => i + 1).map((d) => (
                    <option key={d} value={d}>
                      {d} / 10
                    </option>
                  ))}
                </select>
              </div>
            </div>
            <div className="border-t border-border pt-4">
              <div className="flex items-baseline justify-between gap-2">
                <p className="text-xs font-medium text-text-muted">Overall readiness</p>
                <span className="font-mono text-xs tabular-nums text-navy">
                  {Math.round(overallReadiness * 100)}%
                </span>
              </div>
              <div className="mt-2">
                <MasteryProgressBar mastery={overallReadiness} label="Overall readiness" />
              </div>
              <details className="mt-3">
                <summary className="cursor-pointer text-xs text-text-muted hover:text-navy">
                  Concept mastery
                </summary>
                <div className="mt-3 grid gap-2">
                  {concepts
                    .slice()
                    .sort(
                      (a, b) =>
                        (a.masteryProbability ?? 0) - (b.masteryProbability ?? 0)
                    )
                    .map((c) => (
                      <ConceptCard key={c.conceptId} concept={c} />
                    ))}
                </div>
              </details>
              {conceptsError && !conceptsLoading && (
                <div className="error-banner mt-3 !mb-0">
                  <span>{conceptsError}</span>
                  <button
                    onClick={fetchMastery}
                    className="ml-auto font-medium hover:underline cursor-pointer"
                  >
                    Retry
                  </button>
                </div>
              )}
            </div>
          </div>
          <div>
            {listLoading ? (
              <div className="flex flex-col gap-2">
                {Array.from({ length: 6 }, (_, i) => (
                  <div key={i} className="h-10 rounded-md bg-border animate-pulse" />
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
          </div>
        </section>
      )}

      {/* LeetCode problem page: description left, editor right */}
      <div className="grid min-w-0 grid-cols-1 items-start gap-5 lg:grid-cols-2">
        <section className="min-w-0">
          {detailLoading && !detail ? (
            <div className="panel flex flex-col gap-4">
              <div className="h-4 w-32 rounded bg-border animate-pulse" />
              <div className="h-7 w-3/4 rounded bg-border animate-pulse" />
              <div className="h-4 w-full rounded bg-border animate-pulse" />
              <div className="h-32 rounded-[10px] bg-border animate-pulse" />
            </div>
          ) : detailNotFound ? (
            <div className="panel flex flex-col items-start gap-3">
              <p className="text-sm text-text-body">
                This question is no longer available.
              </p>
              <button
                onClick={() => updateParams({ question: undefined })}
                className="cursor-pointer text-sm font-medium text-navy underline underline-offset-2"
              >
                Pick another question
              </button>
            </div>
          ) : detailError ? (
            <FetchError message={detailError} onRetry={retryDetail} />
          ) : detail ? (
            <QuestionCard key={detail.id} detail={detail} position={position} />
          ) : (
            <div className="panel flex flex-col items-start">
              <p className="text-sm text-text-muted">Select a question to see it here.</p>
            </div>
          )}
        </section>

        <section className="flex min-w-0 flex-col lg:h-[calc(100vh-13rem)] lg:min-h-[520px]">
          {detailLoading && !detail ? (
            <div className="panel flex h-full flex-col gap-4">
              <div className="h-5 w-28 rounded bg-border animate-pulse" />
              <div className="min-h-0 flex-1 rounded-[10px] bg-border animate-pulse" />
              <div className="h-9 rounded-[10px] bg-border animate-pulse" />
            </div>
          ) : detailNotFound ? (
            <div className="panel flex h-full flex-col items-center justify-center gap-3 text-center">
              <p className="text-sm text-text-body">
                This question is no longer available.
              </p>
              <button
                onClick={() => updateParams({ question: undefined })}
                className="cursor-pointer text-sm font-medium text-navy underline underline-offset-2"
              >
                Pick another question
              </button>
            </div>
          ) : detailError ? (
            <FetchError message={detailError} onRetry={retryDetail} />
          ) : detail ? (
            <form onSubmit={handleSubmit} className="panel flex h-full min-h-0 flex-col gap-3">
              {/* One toolbar strip: active file on the left, language on the right. */}
              <div className="flex items-center justify-between gap-4">
                <span className="font-mono text-xs text-text-body">
                  solution.{FILE_EXTENSIONS[language] ?? language}
                </span>
                <div className="w-32">
                  <label htmlFor="language-selector" className="sr-only">
                    Language
                  </label>
                  <LanguageSelector value={language} onChange={handleLanguageChange} />
                </div>
              </div>

              {/* The editor is the one rich surface in the pane. */}
              <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-[10px] border border-border">
                <CodeEditor
                  language={language}
                  value={code}
                  onChange={(newValue) => {
                    updateCodeDraft(newValue ?? "");
                    setSubmitResult(null);
                    setSubmitError(null);
                    // Invalidate any in-flight submission: an edit makes its
                    // identity stale, so its result must not surface later.
                    submitIdentity.current++;
                  }}
                />
              </div>

              {/* Verdict console: real testcase inputs and real submission fields. */}
              <div className="overflow-hidden rounded-[10px] border border-border">
                <div className="flex items-center justify-between gap-3 border-b border-border bg-bg-light pl-3">
                  <div role="tablist" className="flex gap-4">
                    {[
                      ...detail.exampleInputs.map((_, i) => `testcase-${i}`),
                      "result",
                    ].map((value) => (
                      <button
                        key={value}
                        type="button"
                        role="tab"
                        aria-selected={editorTab === value}
                        onClick={() => setEditorTab(value)}
                        className={`-mb-px cursor-pointer border-b-2 py-2 text-xs font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy ${
                          editorTab === value
                            ? "border-navy text-navy"
                            : "border-transparent text-text-muted hover:text-navy"
                        }`}
                      >
                        {value === "result"
                          ? "Result"
                          : `Testcase ${Number(value.split("-")[1]) + 1}`}
                      </button>
                    ))}
                  </div>
                  {submitResult?.questionId === detail.id && (
                    <span className="pr-3 font-mono text-xs tabular-nums text-text-muted">
                      {submitResult.result.testCasesPassed}/
                      {submitResult.result.testCasesTotal}
                    </span>
                  )}
                </div>

                {editorTab === "result" ? (
                  submitResult?.questionId === detail.id ? (
                    (() => {
                      const r = submitResult.result;
                      const verdictColor =
                        r.isCorrect === true
                          ? "text-[#15803d]"
                          : r.isCorrect === false
                            ? "text-[#b91c1c]"
                            : "text-text-body";
                      return (
                        <div className="px-3 py-3">
                          <p className={`text-sm font-medium ${verdictColor}`}>
                            {statusLabel(r, r.isCorrect)}
                          </p>
                          <div className="mt-3 grid grid-cols-3 gap-4">
                            <div>
                              <p className="text-xs text-text-muted">Test cases</p>
                              <p className="font-mono text-sm tabular-nums text-navy">
                                {r.testCasesPassed} / {r.testCasesTotal}
                              </p>
                            </div>
                            <div>
                              <p className="text-xs text-text-muted">Runtime</p>
                              <p className="font-mono text-sm tabular-nums text-navy">
                                {r.executionTimeMs == null
                                  ? "n/a"
                                  : `${r.executionTimeMs} ms`}
                              </p>
                            </div>
                            <div>
                              <p className="text-xs text-text-muted">Memory</p>
                              <p className="font-mono text-sm tabular-nums text-navy">
                                {show(formatMemory(r.memoryUsageKb))}
                              </p>
                            </div>
                          </div>
                          <details className="mt-3 border-t border-border pt-2">
                            <summary className="cursor-pointer text-xs text-text-muted hover:text-navy">
                              Full result
                            </summary>
                            <div className="mt-2">
                              <ResultPanel result={r} />
                            </div>
                          </details>
                        </div>
                      );
                    })()
                  ) : (
                    <p className="px-3 py-4 text-xs text-text-muted">
                      {submitError
                        ? "No result for this submission."
                        : "Submit your code to see the result."}
                    </p>
                  )
                ) : (
                  <div className="px-3 py-3">
                    <p className="mb-1 text-xs text-text-muted">Input</p>
                    <pre className="max-h-24 overflow-auto whitespace-pre-wrap font-mono text-xs leading-relaxed text-text-body">
                      {detail.exampleInputs[
                        Number(editorTab.split("-")[1])
                      ] ?? "No sample input"}
                    </pre>
                  </div>
                )}
              </div>

              {submitError && (
                <p role="alert" className="error-banner !mb-0">
                  {submitError}
                </p>
              )}

              <div className="flex items-center justify-between gap-3 border-t border-border pt-3">
                <span className="flex items-center gap-2 text-xs text-text-muted">
                  <span
                    aria-hidden="true"
                    className={`h-1.5 w-1.5 rounded-full ${runStatus.dot}`}
                  />
                  <span className="font-mono tabular-nums">{runStatus.text}</span>
                </span>
                <button
                  type="submit"
                  disabled={submitting || !detail?.id}
                  className="btn-solid"
                >
                  {submitting ? (
                    <span className="flex items-center justify-center gap-2">
                      <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                      </svg>
                      Submitting
                    </span>
                  ) : (
                    "Submit code"
                  )}
                </button>
              </div>
            </form>
          ) : (
            <div className="panel flex h-full flex-col items-center justify-center">
              <p className="text-sm text-text-muted">
                Select a question to open the editor.
              </p>
            </div>
          )}
        </section>
      </div>
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
      <div className="min-h-screen flex flex-col bg-bg-light">
        <Navbar />
        <main className="mx-auto flex w-full max-w-[1400px] flex-1 flex-col px-8 py-10">
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

