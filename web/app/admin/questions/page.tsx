"use client";

import { Suspense, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import {
  api,
  ApiError,
  type AdminQuestion,
  type AdminQuestionListResult,
} from "@/lib/api";
import { useAuth } from "@/lib/auth";
import FetchError from "@/components/FetchError";
import {
  computeTotalPages,
  isGuid,
  parseDifficulty,
  parsePageParam,
  splitEntries,
  validateRubricLines,
  validateTestCaseLines,
} from "@/lib/question-form";

const PAGE_SIZE = 20; // server caps pageSize at 100

const SAMPLE_TEST_CASE = '{"input": "3 4", "expected": "7"}';

export default function AdminQuestionsPage() {
  // #102: guard, Navbar and sidebar come from app/admin/layout.tsx
  return (
    <Suspense fallback={<QuestionsSkeleton />}>
      <QuestionsContent />
    </Suspense>
  );
}

type ListState =
  | { key: string; ok: true; result: AdminQuestionListResult }
  | { key: string; ok: false; message: string };

function isContentAdminRoles(roles: string[]): boolean {
  return roles.includes("SuperAdmin") || roles.includes("ContentAdmin");
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? "—"
    : d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
}

function QuestionsContent() {
  const { user, setUser } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();

  const page = parsePageParam(searchParams.get("page"));

  const [list, setList] = useState<ListState | null>(null);
  const [reload, setReload] = useState(0);
  const [panelOpen, setPanelOpen] = useState(false);
  const [success, setSuccess] = useState<{ page: number; text: string } | null>(null);

  const isContentAdmin =
    !!user?.roles?.includes("SuperAdmin") || !!user?.roles?.includes("ContentAdmin");

  // keyed results: loading/error are derived, never setState synchronously in an effect
  const key = `${page}|${reload}`;
  const state = list?.key === key ? list : null;
  const loading = state === null;
  const loadError = state && !state.ok ? state.message : null;
  const result = state?.ok ? state.result : null;
  const questions = result?.questions ?? [];
  const totalCount = result?.totalCount ?? 0;
  const totalPages = computeTotalPages(totalCount, PAGE_SIZE);

  // Deliberate guard layering (#82): the shell admits any admin role, but this
  // page maps the ManageContent policy (ContentAdmin, SuperAdmin) — the server
  // 403s everyone else, this redirect just keeps the UI honest. Anonymous
  // routing is AdminRoute's + AuthProvider's job.
  useEffect(() => {
    if (user && !isContentAdmin) router.replace("/dashboard");
  }, [user, isContentAdmin, router]);

  useEffect(() => {
    if (!isContentAdmin) return;
    let cancelled = false;
    api.adminQuestions(page, PAGE_SIZE)
      .then((result) => {
        if (cancelled) return;
        if (page > computeTotalPages(result.totalCount, PAGE_SIZE)) {
          // correction, not navigation: replace so Back doesn't land on the dead URL
          router.replace("/admin/questions");
          return;
        }
        setList({ key, ok: true, result });
      })
      .catch((e) => {
        if (cancelled) return;
        // stale local auth (expired session or server-side demotion): re-verify
        // instead of a dead-end retry — me() 401s -> setUser(null) -> AuthProvider
        // routes to /login; a demoted caller gets refreshed roles and the guard reroutes.
        if (e instanceof ApiError && (e.status === 401 || e.status === 403)) {
          api
            .me()
            .then((me) => {
              if (cancelled) return;
              setUser(me);
              // refresh didn't explain the failure: surface retry instead of a
              // stuck skeleton
              if (isContentAdminRoles(me.roles)) {
                setList({ key, ok: false, message: e.message });
              }
            })
            .catch(() => {
              if (!cancelled) setUser(null);
            });
          return;
        }
        setList({
          key,
          ok: false,
          message: e instanceof ApiError ? e.message : "Failed to load questions.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [isContentAdmin, page, key, setUser, router]);

  function goToPage(next: number) {
    setSuccess(null); // same-route navigation keeps this component mounted
    router.push(next <= 1 ? "/admin/questions" : `/admin/questions?page=${next}`);
  }

  function openPanel() {
    setSuccess(null);
    setPanelOpen(true);
  }

  function onCreated() {
    // banner belongs to the destination, not the origin: page-1 refetch shows
    // the new row, and Back from page 1 no longer resurrects a stale banner
    setSuccess({ page: 1, text: "Question created." });
    setPanelOpen(false);
    // newest-first list: show the new row by refetching page 1
    if (page > 1) router.push("/admin/questions");
    else setReload((n) => n + 1);
  }

  if (!user || !isContentAdmin) return null;

  return (
    <>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-heading font-bold text-text-heading">Question bank</h1>
          {state?.ok && (
            <p className="text-sm text-text-muted mt-1">
              {totalCount} question{totalCount === 1 ? "" : "s"}, page {page} of {totalPages}
            </p>
          )}
        </div>
        <button
          onClick={openPanel}
          className="px-3 py-1.5 text-sm font-semibold text-white bg-primary hover:bg-primary-dark rounded-xl transition-colors cursor-pointer"
        >
          New question
        </button>
      </div>

      {success && success.page === page && (
        <div
          role="status"
          className="flex items-center gap-2.5 rounded-xl border border-[#BBF7D0] bg-[#F0FDF4] px-4 py-3 text-sm font-medium text-[#15803D] mb-5"
        >
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <path d="M20 6 9 17l-5-5" />
          </svg>
          {success.text}
        </div>
      )}

      {loading && <QuestionsSkeleton />}
      {loadError && (
        <FetchError message={loadError} onRetry={() => setReload((n) => n + 1)} />
      )}

      {panelOpen && <CreateQuestionForm onCreated={onCreated} onClose={() => setPanelOpen(false)} />}

      {state?.ok &&
        (questions.length === 0 ? (
          <div className="card text-center" role="status">
            <p className="text-sm font-medium text-text-heading">
              {totalCount === 0 ? "No questions yet." : "No questions on this page."}
            </p>
            {totalCount === 0 && (
              <p className="text-sm text-text-muted mt-1">
                {panelOpen ? "Create the first one in the form above." : 'Create the first one with "New question".'}
              </p>
            )}
          </div>
        ) : (
          <>
            {/* card stack below md */}
            <div className="md:hidden space-y-4">
              {questions.map((q) => (
                <QuestionCard key={q.id} q={q} />
              ))}
            </div>

            {/* table at md+ */}
            <div className="hidden md:block rounded-2xl border border-border bg-bg-card overflow-x-auto">
              <table className="w-full text-sm">
                <caption className="sr-only">Questions, newest first</caption>
                <thead>
                  <tr className="bg-bg-light text-left text-xs font-semibold uppercase tracking-wider text-text-muted">
                    <th scope="col" className="px-4 py-3">Title</th>
                    <th scope="col" className="px-4 py-3">Concept</th>
                    <th scope="col" className="px-4 py-3">Difficulty</th>
                    <th scope="col" className="px-4 py-3">Test cases</th>
                    <th scope="col" className="px-4 py-3">Created</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {questions.map((q) => (
                    <tr key={q.id} className="hover:bg-bg-light transition-colors">
                      <td className="px-4 py-3 max-w-[22rem]">
                        <p className="font-medium text-text-heading truncate" title={q.title}>
                          {q.title}
                        </p>
                        <p className="text-xs text-text-muted truncate" title={q.description}>
                          {q.description}
                        </p>
                      </td>
                      <td className="px-4 py-3 font-mono text-xs text-text-body">
                        <span title={q.conceptId}>{q.conceptId.slice(0, 8)}…</span>
                      </td>
                      <td className="px-4 py-3"><DifficultyBadge value={q.difficulty} /></td>
                      <td className="px-4 py-3 text-text-body">{q.testCaseCount}</td>
                      <td className="px-4 py-3 text-text-body whitespace-nowrap">
                        {formatDate(q.createdAt)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <nav className="flex items-center justify-between mt-4" aria-label="Pagination">
              <button
                onClick={() => goToPage(page - 1)}
                disabled={page === 1}
                className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
              >
                Previous
              </button>
              <span className="text-sm text-text-muted">
                Page {page} of {totalPages}
              </span>
              <button
                onClick={() => goToPage(page + 1)}
                disabled={page >= totalPages}
                className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
              >
                Next
              </button>
            </nav>
          </>
        ))}
    </>
  );
}

function DifficultyBadge({ value }: { value: number }) {
  return (
    <span className="inline-flex items-center rounded-full bg-accent-light-blue px-2.5 py-1 text-xs font-semibold text-primary">
      {value}/10
    </span>
  );
}

function QuestionCard({ q }: { q: AdminQuestion }) {
  return (
    <div className="rounded-2xl border border-border bg-bg-card p-4 space-y-2">
      <div className="flex items-start justify-between gap-3">
        <p className="font-medium text-text-heading">{q.title}</p>
        <DifficultyBadge value={q.difficulty} />
      </div>
      <p className="text-sm text-text-body line-clamp-2">{q.description}</p>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-text-muted">
        <div className="flex gap-1 min-w-0">
          <dt className="shrink-0">Concept:</dt>
          <dd className="truncate font-mono">
            <span title={q.conceptId}>{q.conceptId.slice(0, 8)}…</span>
          </dd>
        </div>
        <div className="flex gap-1">
          <dt className="shrink-0">Test cases:</dt>
          <dd className="text-text-body">{q.testCaseCount}</dd>
        </div>
        <div className="flex gap-1 col-span-2">
          <dt className="shrink-0">Created:</dt>
          <dd className="text-text-body">{formatDate(q.createdAt)}</dd>
        </div>
      </dl>
    </div>
  );
}

function FieldError({ id, message }: { id: string; message: string | undefined }) {
  if (!message) return null;
  return (
    <p id={id} role="alert" className="mt-1.5 text-xs font-medium text-[#B91C1C]">
      {message}
    </p>
  );
}

// Error keys mirror the FluentValidation property names so server field errors
// (ApiError.errors) map onto the same state used by local validation.
function CreateQuestionForm({
  onCreated,
  onClose,
}: {
  onCreated: () => void;
  onClose: () => void;
}) {
  const { setUser } = useAuth();
  const router = useRouter();

  const [conceptId, setConceptId] = useState("");
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [difficulty, setDifficulty] = useState("");
  const [questionType, setQuestionType] = useState("Code");
  const [testCasesText, setTestCasesText] = useState("");
  const [rubricText, setRubricText] = useState("");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const headingRef = useRef<HTMLHeadingElement | null>(null);

  // move focus to the panel heading when it opens (conditional render)
  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  function validate(
    conceptIdValue: string,
    difficultyValue: number | null,
    testCaseEntries: string[],
    rubricEntries: string[]
  ): Record<string, string> {
    const errors: Record<string, string> = {};
    if (!isGuid(conceptIdValue)) {
      errors.ConceptId = "Concept ID is required and must be a valid GUID.";
    }
    if (!title.trim()) errors.Title = "Title is required.";
    if (!description.trim()) errors.Description = "Description is required.";
    if (difficultyValue === null) {
      errors.Difficulty = "Difficulty must be between 1 and 10.";
    }
    if (testCaseEntries.length === 0) {
      errors.TestCases = "At least one test case is required.";
    } else {
      const testCaseErrors = validateTestCaseLines(testCaseEntries);
      if (testCaseErrors.length > 0) errors.TestCases = testCaseErrors.join(" ");
    }
    const rubricErrors = validateRubricLines(rubricEntries);
    if (rubricErrors.length > 0) errors.Rubric = rubricErrors.join(" ");
    return errors;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (submitting) return;

    // client-side mirror of CreateQuestionValidator — avoids a round trip for
    // errors the server would reject anyway; server errors still take precedence.
    const parsedDifficulty = parseDifficulty(difficulty);
    const testCaseEntries = splitEntries(testCasesText);
    const rubricEntries = splitEntries(rubricText);
    const errors = validate(conceptId.trim(), parsedDifficulty, testCaseEntries, rubricEntries);
    if (Object.keys(errors).length > 0 || parsedDifficulty === null) {
      setFieldErrors(errors);
      setBanner(null); // never let a previous attempt's banner outlive the fix
      return;
    }
    setFieldErrors({});
    setBanner(null);
    setSubmitting(true);
    try {
      await api.adminCreateQuestion({
        conceptId: conceptId.trim(),
        title: title.trim(),
        description: description.trim(),
        difficulty: parsedDifficulty,
        questionType,
        testCases: testCaseEntries,
        ...(rubricEntries.length > 0 ? { rubric: rubricEntries } : {}),
      });
      onCreated();
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        // stale identity: re-verify so demotion reroutes via the page guard
        api
          .me()
          .then((me) => {
            setUser(me);
            if (!isContentAdminRoles(me.roles)) {
              router.replace("/dashboard");
              return;
            }
            // refresh didn't explain the failure: show it
            setBanner(err.message);
          })
          .catch(() => {
            // me() 401s -> setUser(null) -> AuthProvider routes to /login
            setUser(null);
          });
      } else if (err instanceof ApiError && err.status === 404) {
        // Not every 404 is a missing concept: a routed miss (an API build without
        // this endpoint) answers 404 as well, so only blame the Concept ID when
        // the server's own message says the concept was the problem.
        if (/concept/i.test(err.message)) {
          setFieldErrors({
            ConceptId: "Concept not found. Check the Concept ID and try again.",
          });
        } else {
          setBanner(err.message);
        }
      } else if (err instanceof ApiError && err.errors && Object.keys(err.errors).length > 0) {
        const mapped: Record<string, string> = {};
        for (const [field, messages] of Object.entries(err.errors)) {
          mapped[field] = messages.join(" ");
        }
        setFieldErrors(mapped);
      } else {
        setBanner(
          err instanceof ApiError ? err.message : "Something went wrong. Please try again."
        );
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="card-flat mb-8" aria-labelledby="new-question-heading">
      <div className="flex items-start justify-between gap-4 mb-6">
        <h2
          id="new-question-heading"
          tabIndex={-1}
          ref={headingRef}
          className="text-xl font-heading font-bold text-text-heading focus:outline-none"
        >
          New question
        </h2>
        <button
          type="button"
          onClick={onClose}
          disabled={submitting}
          className="px-3 py-1.5 text-sm font-medium text-text-muted hover:text-text-heading hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Cancel
        </button>
      </div>

      <form onSubmit={handleSubmit} noValidate>
        {banner && (
          <div className="error-banner" role="alert">
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <circle cx="12" cy="12" r="10" />
              <line x1="15" x2="9" y1="9" y2="15" />
              <line x1="9" x2="15" y1="9" y2="15" />
            </svg>
            {banner}
          </div>
        )}

        <div className="grid gap-x-6 sm:grid-cols-2">
          <div className="input-group">
            <label htmlFor="q-title">Title</label>
            <input
              id="q-title"
              type="text"
              value={title}
              maxLength={300}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="e.g. Merge two sorted lists"
              aria-invalid={!!fieldErrors.Title}
              aria-describedby={fieldErrors.Title ? "q-title-error" : undefined}
            />
            <FieldError id="q-title-error" message={fieldErrors.Title} />
          </div>

          <div className="input-group">
            <label htmlFor="q-conceptId">Concept ID</label>
            <input
              id="q-conceptId"
              type="text"
              value={conceptId}
              onChange={(e) => setConceptId(e.target.value)}
              placeholder="e.g. 3f2504e0-4f89-11d3-9a0c-0305e82c3301"
              aria-invalid={!!fieldErrors.ConceptId}
              aria-describedby={fieldErrors.ConceptId ? "q-conceptId-error" : undefined}
            />
            <FieldError id="q-conceptId-error" message={fieldErrors.ConceptId} />
          </div>
        </div>

        <div className="input-group">
          <label htmlFor="q-description">Description</label>
          <textarea
            id="q-description"
            value={description}
            rows={4}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Full problem statement shown to students"
            className="resize-y"
            aria-invalid={!!fieldErrors.Description}
            aria-describedby={fieldErrors.Description ? "q-description-error" : undefined}
          />
          <FieldError id="q-description-error" message={fieldErrors.Description} />
        </div>

        <div className="grid gap-x-6 sm:grid-cols-2">
          <div className="input-group">
            <label htmlFor="q-difficulty">Difficulty (1–10)</label>
            <input
              id="q-difficulty"
              type="number"
              inputMode="numeric"
              min={1}
              max={10}
              step={1}
              value={difficulty}
              onChange={(e) => setDifficulty(e.target.value)}
              placeholder="e.g. 5"
              aria-invalid={!!fieldErrors.Difficulty}
              aria-describedby={fieldErrors.Difficulty ? "q-difficulty-error" : undefined}
            />
            <FieldError id="q-difficulty-error" message={fieldErrors.Difficulty} />
          </div>

          <div className="input-group">
            <label htmlFor="q-type">Question type</label>
            <select
              id="q-type"
              value={questionType}
              onChange={(e) => setQuestionType(e.target.value)}
            >
              <option value="Code">Code</option>
              <option value="MCQ">MCQ</option>
            </select>
          </div>
        </div>

        <div className="input-group">
          <label htmlFor="q-testCases">Test cases</label>
          <textarea
            id="q-testCases"
            value={testCasesText}
            rows={4}
            onChange={(e) => setTestCasesText(e.target.value)}
            className="resize-y font-mono text-sm"
            aria-invalid={!!fieldErrors.TestCases}
            aria-describedby={fieldErrors.TestCases ? "q-testCases-hint q-testCases-error" : "q-testCases-hint"}
          />
          <p id="q-testCases-hint" className="mt-1.5 text-xs text-text-muted">
            One JSON object per line, e.g.{" "}
            <code className="font-mono">{SAMPLE_TEST_CASE}</code>. Optional{" "}
            <code className="font-mono">{'"hidden": true'}</code> entries must come last.
          </p>
          <FieldError id="q-testCases-error" message={fieldErrors.TestCases} />
        </div>

        <div className="input-group">
          <label htmlFor="q-rubric">
            Rubric <span className="font-normal text-text-muted">(optional)</span>
          </label>
          <textarea
            id="q-rubric"
            value={rubricText}
            rows={3}
            onChange={(e) => setRubricText(e.target.value)}
            className="resize-y font-mono text-sm"
            aria-invalid={!!fieldErrors.Rubric}
            aria-describedby={fieldErrors.Rubric ? "q-rubric-hint q-rubric-error" : "q-rubric-hint"}
          />
          <p id="q-rubric-hint" className="mt-1.5 text-xs text-text-muted">
            One JSON object per line, e.g.{" "}
            <code className="font-mono">{'{ "criterion": "Handles overflow", "points": 2 }'}</code>.
          </p>
          <FieldError id="q-rubric-error" message={fieldErrors.Rubric} />
        </div>

        <button
          type="submit"
          disabled={submitting}
          className="px-5 py-2.5 text-sm font-semibold text-white bg-primary hover:bg-primary-dark rounded-xl transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {submitting ? (
            <span className="flex items-center justify-center gap-2">
              <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Creating...
            </span>
          ) : (
            "Create question"
          )}
        </button>
      </form>
    </section>
  );
}

function QuestionsSkeleton() {
  return (
    <div className="card" aria-hidden="true">
      <div className="space-y-5">
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className="space-y-2">
            <div className="h-4 w-48 bg-border rounded animate-pulse" />
            <div className="h-3 w-32 bg-border rounded animate-pulse" />
          </div>
        ))}
      </div>
    </div>
  );
}
