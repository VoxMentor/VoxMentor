"use client";

import { useCallback, useEffect, useState } from "react";
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
  const { user } = useAuth();
  const router = useRouter();
  const [submissionId, setSubmissionId] = useState<string | null>(null);
  const [submissionIdResolved, setSubmissionIdResolved] = useState(false);

  // Defense-in-depth: shell admits any admin role, this page is
  // PlatformAdmin/SuperAdmin only per the ManagePlatform policy.
  const isPlatformAdmin =
    !!user && user.roles.some((role) => PLATFORM_ADMINS.includes(role));

  useEffect(() => {
    if (user && !isPlatformAdmin) router.replace("/dashboard");
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
          if (!signal?.aborted) {
            setError(
              e instanceof ApiError ? e.message : "Failed to load submission."
            );
          }
        })
        .finally(() => {
          if (!signal?.aborted) setLoading(false);
        });
    },
    [submissionId]
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
      className="text-xs text-primary hover:text-primary-dark underline underline-offset-4"
    >
      ← Back to submissions
    </Link>
  );

  if (loading) {
    return (
      <div className="flex items-center justify-center py-24">
        <div className="h-8 w-24 bg-border rounded animate-pulse" />
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
            const controller = new AbortController();
            loadSubmission(controller.signal);
          }}
        />
      </div>
    );
  }

  const score = submission.plagiarismScore;
  const scoreColor =
    score == null
      ? "text-text-muted"
      : score >= 0.7
        ? "text-red-600"
        : score >= 0.4
          ? "text-yellow-600"
          : "text-green-600";

  return (
    <div>
      <div className="mb-8 flex items-center justify-between">
        <div>{backLink}</div>
        <div className="text-right">
          <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
            Plagiarism review
          </h1>
          <p className="text-text-muted mt-2">
            Submission: {submission.id} &nbsp;•&nbsp; User:{" "}
            {submission.userEmail ?? submission.userId}
          </p>
        </div>
      </div>

      <div className="flex flex-col gap-6">
        <div className="card p-4">
          <div className="flex items-center justify-between mb-4">
            <span className="font-medium text-navy text-sm">
              Submission details
            </span>
            <span className={`text-xs font-medium ${scoreColor}`}>
              Similarity: {score != null ? `${(score * 100).toFixed(1)}%` : "—"}
            </span>
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
            <div className="bg-white p-3 rounded-lg border border-border">
              <span className="text-xs text-text-muted uppercase tracking-wider">
                Question
              </span>
              <p className="text-sm text-text-body mt-1 truncate">
                {submission.questionTitle ?? submission.questionId}
              </p>
            </div>
            <div className="bg-white p-3 rounded-lg border border-border">
              <span className="text-xs text-text-muted uppercase tracking-wider">
                Language
              </span>
              <p className="text-sm text-text-body mt-1">
                {submission.language}
              </p>
            </div>
            <div className="bg-white p-3 rounded-lg border border-border">
              <span className="text-xs text-text-muted uppercase tracking-wider">
                Status
              </span>
              <p className="text-sm text-text-body mt-1">
                {SUBMISSION_STATUS_LABELS[submission.status] ??
                  String(submission.status)}
              </p>
            </div>
            <div className="bg-white p-3 rounded-lg border border-border">
              <span className="text-xs text-text-muted uppercase tracking-wider">
                Test cases
              </span>
              <p className="text-sm text-text-body mt-1">
                {submission.testCasesPassed}/{submission.testCasesTotal}
              </p>
            </div>
            <div className="bg-white p-3 rounded-lg border border-border">
              <span className="text-xs text-text-muted uppercase tracking-wider">
                Created
              </span>
              <p className="text-sm text-text-body mt-1">
                {new Date(submission.createdAt).toLocaleDateString(undefined, {
                  month: "short",
                  day: "numeric",
                  year: "numeric",
                })}
              </p>
            </div>
            <div className="bg-white p-3 rounded-lg border border-border">
              <span className="text-xs text-text-muted uppercase tracking-wider">
                User ID
              </span>
              <p className="text-sm text-text-body mt-1 truncate">
                {submission.userId}
              </p>
            </div>
          </div>
        </div>

        <div>
          {/* The queue is read-only by design (see PlagiarismReviewController):
              the admin compares this flagged source against peer submissions. */}
          <h2 className="text-sm font-medium text-text-heading uppercase tracking-wider mb-3">
            Submitted code
          </h2>
          <div className="p-4 rounded-lg bg-white border border-border">
            <pre className="whitespace-pre-wrap text-sm text-text-body font-mono">
              {submission.code}
            </pre>
          </div>
        </div>

        {submission.aiEvaluation && (
          <div>
            <h2 className="text-sm font-medium text-text-heading uppercase tracking-wider mb-3">
              AI evaluation
            </h2>
            <div className="p-4 rounded-lg bg-white border border-border">
              <div className="whitespace-pre-wrap text-sm text-text-body">
                {submission.aiEvaluation}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
