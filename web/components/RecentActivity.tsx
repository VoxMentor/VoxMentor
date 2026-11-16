"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, ApiError, type SubmissionItem } from "@/lib/api";
import FetchError from "@/components/FetchError";

function timeAgo(iso: string): string {
  const seconds = Math.floor((Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  return new Date(iso).toLocaleDateString();
}

function Skeleton() {
  return (
    <div className="panel">
      <div className="mb-4 h-5 w-32 animate-pulse rounded bg-border" />
      <div className="space-y-3.5">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="flex items-center gap-3">
            <div className="h-1.5 w-1.5 animate-pulse rounded-full bg-border" />
            <div className="flex-1 space-y-1.5">
              <div className="h-3 w-40 animate-pulse rounded bg-border" />
              <div className="h-3 w-24 animate-pulse rounded bg-border" />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/**
 * Recent activity feed, newest first. Each row is a submission: question,
 * concept and relative time, with a pass/fail dot and the mastery change.
 */
export default function RecentActivity() {
  const [items, setItems] = useState<SubmissionItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    // ponytail: 6 rows fits the sidebar column; raise it if this becomes a feed page.
    api
      .submissions(6)
      .then(setItems)
      .catch((e) =>
        setError(e instanceof ApiError ? e.message : "Failed to load activity.")
      )
      .finally(() => setLoading(false));
  }, []);

  const retry = () => {
    setLoading(true);
    setError(null);
    load();
  };

  useEffect(() => {
    load();
  }, [load]);

  if (loading) return <Skeleton />;
  if (error) return <FetchError message={error} onRetry={retry} />;
  if (!items) return null;

  return (
    <div className="panel">
      <h2 className="font-heading text-base font-semibold text-navy">
        Recent activity
      </h2>

      {items.length === 0 ? (
        <div className="mt-4">
          <p className="text-sm leading-relaxed text-text-body">
            No submissions yet. Your practice results will show up here.
          </p>
          <Link
            href="/practice"
            className="mt-2 inline-block text-sm font-medium text-primary hover:text-primary-dark"
          >
            Start a practice session
          </Link>
        </div>
      ) : (
        <ul className="mt-4 space-y-3.5">
          {items.map((item) => {
            const delta =
              item.masteryDelta === null
                ? null
                : `${item.masteryDelta >= 0 ? "+" : ""}${Math.round(
                    item.masteryDelta * 100
                  )}%`;

            return (
              <li key={item.submissionId} className="flex items-start gap-3">
                <span
                  aria-hidden="true"
                  className={`mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${
                    item.isCorrect ? "bg-success" : "bg-danger"
                  }`}
                />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm text-navy">
                    {item.questionTitle}
                  </p>
                  <p className="truncate text-xs text-text-muted">
                    {item.conceptName} · {timeAgo(item.createdAt)}
                  </p>
                </div>
                <span className="shrink-0 text-right text-xs leading-relaxed text-text-muted">
                  {item.isCorrect ? "Pass" : "Fail"}
                  {delta && ` · ${delta}`}
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
