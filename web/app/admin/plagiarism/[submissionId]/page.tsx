"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  api,
  ApiError,
  SUBMISSION_STATUS_LABELS,
  type AdminPlagiarismSubmissionDetail,
} from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { PLATFORM_ADMINS } from "@/lib/admin-nav";
import FetchError from "@/components/FetchError";

export default function AdminPlagiarismSubmissionPage({
  params,
}: {
  params: Promise<{ submissionId: string }>;
}) {
  const { user, setUser } = useAuth();
  const router = useRouter();
  const [submissionId, setSubmissionId] = useState<string | null>(null);
  const [submissionIdResolved, setSubmissionIdResolved] = useState(false);

  // Defense-in-depth: shell admits any admin role, this page is
  // PlatformAdmin/SuperAdmin only per the ManagePlatform policy.
  const isPlatformAdmin =
    !!user && user.roles.some((role) => PLATFORM_ADMINS.includes(role));

  useEffect(() => {
    if (user && !isPlatformAdmin) router.replace("/admin");
  }, [user, isPlatformAdmin, router]);

  useEffect(() => {
    params
      .then((p) => {
        setSubmissionId(p.submissionId);
        setSubmissionIdResolved(true);
      })
      .catch(() => {
        setSubmissionIdResolved(true);
      });
  }, [params]);

  const [submission, setSubmission] =
    useState<AdminPlagiarismSubmissionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Retry requests are not tied to the effect's controller, so track them
  // separately: abort on unmount so a late me() can't overwrite shared auth.
  const retryController = useRef<AbortController | null>(null);

  useEffect(() => {
    return () => retryController.current?.abort();
  }, []);

  const loadSubmission = useCallback(
    (signal?: AbortSignal) => {
      if (!submissionId) return;
      setLoading(true);
      setError(null);
      api
        .adminPlagiarismSubmission(submissionId)
        .then((result) => {
          if (!signal?.aborted) setSubmission(result);
        })
        .catch((e) => {
          if (signal?.aborted) return;
          // stale local auth (expired session or server-side demotion): re-verify
          // instead of a dead-end retry. me() 401s -> setUser(null) -> AuthProvider
          // routes to /login; a demoted caller gets refreshed roles and the guard reroutes.
          if (e instanceof ApiError && (e.status === 401 || e.status === 403)) {
            return api
              .me()
              .then((me) => {
                if (signal?.aborted) return;
                setUser(me);
                if (me.roles.some((role) => PLATFORM_ADMINS.includes(role))) {
                  setError(e.message);
                }
              })
              .catch((meErr) => {
                if (signal?.aborted) return;
                if (meErr instanceof ApiError && meErr.status === 401) {
                  setUser(null);
                } else {
                  setError(e.message);
                }
              });
          }
          setError(
            e instanceof ApiError ? e.message : "Failed to load submission."
          );
        })
        .finally(() => {
          if (!signal?.aborted) setLoading(false);
        });
    },
    [submissionId, setUser]
  );

  useEffect(() => {
    // Don't start loading until submissionId is resolved
    if (!submissionIdResolved) return;
    if (!submissionId) {
      // Use setTimeout to defer setState calls, avoiding the rule violation
      setTimeout(() => {
        setLoading(false);
        setError("Invalid submission ID.");
      }, 0);
      return;
    }
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial data fetch
    loadSubmission(controller.signal);
    return () => controller.abort();
  }, [submissionId, submissionIdResolved, loadSubmission]);

  if (!user || !isPlatformAdmin) return null;

  const backLink = (
    <Link
      href="/admin/plagiarism"
      className="inline-block text-xs text-navy underline underline-offset-4 transition-colors hover:text-primary-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy"
    >
      ← Back to submissions
    </Link>
  );

  if (loading) {
    return (
      <div className="panel" aria-hidden="true">
        <div className="h-4 w-24 bg-border rounded animate-pulse" />
      </div>
    );
  }

  if (error || !submission) {
    // Back link stays available on failure too, mirroring the success header,
    // so the admin isn't stranded on a dead end.
    return (
      <div>
        <div className="mb-4">{backLink}</div>
        <FetchError
          message={error || "Submission not found."}
          onRetry={() => {
            retryController.current?.abort();
            const controller = new AbortController();
            retryController.current = controller;
            loadSubmission(controller.signal);
          }}
        />
      </div>
    );
  }

  const score = submission.plagiarismScore;
  const severity =
    score == null
      ? "n/a"
      : score >= 0.7
        ? "high"
        : score >= 0.4
          ? "medium"
          : "low";

  return (
    <div>
      <div className="mb-4">{backLink}</div>

      <header className="mb-8">
        <h1 className="font-heading text-2xl font-semibold text-navy">
          Plagiarism review
        </h1>
        <p className="mt-1 max-w-2xl text-sm text-text-muted">
          User {submission.userEmail ?? submission.userId}
        </p>
      </header>

      <div className="flex flex-col gap-6">
        <div className="panel">
          <div className="flex items-baseline justify-between gap-4 border-b border-border pb-4">
            <h2 className="text-sm font-medium text-navy">Submission details</h2>
            <p className="text-right">
              <span className="font-mono text-xl tabular-nums text-navy">
                {score != null ? `${(score * 100).toFixed(1)}%` : "n/a"}
              </span>
              <span className="mt-0.5 block text-xs text-text-muted">
                similarity ({severity})
              </span>
            </p>
          </div>
          <dl className="mt-4 grid grid-cols-1 gap-x-8 gap-y-4 sm:grid-cols-2 lg:grid-cols-3">
            <Field label="Question" value={submission.questionTitle ?? submission.questionId} />
            <Field label="Language" value={submission.language} />
            <Field
              label="Status"
              value={SUBMISSION_STATUS_LABELS[submission.status] ?? String(submission.status)}
            />
            <Field
              label="Test cases"
              value={`${submission.testCasesPassed}/${submission.testCasesTotal}`}
              numeric
            />
            <Field
              label="Created"
              value={new Date(submission.createdAt).toLocaleDateString(undefined, {
                month: "short",
                day: "numeric",
                year: "numeric",
              })}
              numeric
            />
            <Field label="User ID" value={submission.userId} mono />
          </dl>
        </div>

        <section>
          {/* The queue is read-only by design (see PlagiarismReviewController):
              the admin compares this flagged source against peer submissions. */}
          <h2 className="mb-3 text-sm font-medium text-navy">Submitted code</h2>
          <div className="panel-sm">
            <pre className="whitespace-pre-wrap text-sm text-text-body font-mono">
              {submission.code}
            </pre>
          </div>
        </section>

        {submission.aiEvaluation && (
          <section>
            <h2 className="mb-3 text-sm font-medium text-navy">AI evaluation</h2>
            <div className="panel-sm">
              <div className="whitespace-pre-wrap text-sm text-text-body">
                {submission.aiEvaluation}
              </div>
            </div>
          </section>
        )}
      </div>
    </div>
  );
}

function Field({
  label,
  value,
  mono = false,
  numeric = false,
}: {
  label: string;
  value: string;
  mono?: boolean;
  numeric?: boolean;
}) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-text-muted">{label}</dt>
      <dd
        title={value}
        className={`mt-1 truncate text-sm text-text-body ${mono ? "font-mono text-xs" : ""} ${numeric ? "tabular-nums" : ""}`}
      >
        {value}
      </dd>
    </div>
  );
}
