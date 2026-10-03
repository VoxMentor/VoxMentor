"use client";

import { useCallback, useEffect, useState } from "react";
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
    <div className="card">
      <div className="h-5 w-40 bg-border rounded animate-pulse mb-4" />
      <div className="space-y-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="h-12 bg-border rounded-xl animate-pulse" />
        ))}
      </div>
    </div>
  );
}

export default function RecentActivity() {
  const [items, setItems] = useState<SubmissionItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    api
      .submissions(10)
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
    <div className="card">
      <h2 className="font-heading font-semibold text-navy text-lg mb-4">
        Recent Activity
      </h2>

      {items.length === 0 ? (
        <p className="text-sm text-text-muted">
          Start practicing to see your activity here
        </p>
      ) : (
        <div className="divide-y divide-border">
          {items.map((item) => (
            <div
              key={item.submissionId}
              className="flex items-center gap-3 py-3"
              title={`${item.questionTitle} — ${timeAgo(item.createdAt)}`}
            >
              <span
                className={`shrink-0 w-2 h-2 rounded-full ${
                  item.isCorrect ? "bg-success" : "bg-danger"
                }`}
              />
              <div className="min-w-0 flex-1">
                <p className="text-sm font-medium text-navy truncate">
                  {item.questionTitle}
                </p>
                <p className="text-xs text-text-muted truncate">
                  {item.conceptName}
                </p>
              </div>
              <span
                className={`shrink-0 text-xs font-medium px-2 py-0.5 rounded-full ${
                  item.isCorrect
                    ? "bg-success/10 text-success"
                    : "bg-danger/10 text-danger"
                }`}
              >
                {item.isCorrect ? "Pass" : "Fail"}
              </span>
              <span
                className={`shrink-0 text-xs font-medium w-12 text-right ${
                  item.masteryDelta === null
                    ? "text-text-muted"
                    : item.masteryDelta >= 0
                      ? "text-success"
                      : "text-danger"
                }`}
              >
                {item.masteryDelta === null
                  ? "—"
                  : `${item.masteryDelta >= 0 ? "+" : ""}${Math.round(item.masteryDelta * 100)}%`}
              </span>
              <span className="shrink-0 text-xs text-text-muted w-20 text-right">
                {timeAgo(item.createdAt)}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
