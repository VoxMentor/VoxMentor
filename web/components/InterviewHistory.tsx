"use client";

import { useEffect, useRef, useState } from "react";
import { api, type MockInterviewHistoryItem } from "@/lib/api";
import FetchError from "@/components/FetchError";

/** Formats an ISO date as "Nov 11, 2026"; passes raw text through if unparsable. */
function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

/** One history page; matches api.mockHistory's default page size. */
const PAGE_SIZE = 20;

/**
 * The student's past mock interviews, newest first. `refreshKey` changes
 * whenever an interview finishes so the list picks up the new row.
 */
export default function InterviewHistory({
  refreshKey = 0,
  selectedId,
  onSelect,
}: {
  refreshKey?: number;
  selectedId?: string | null;
  onSelect?: (item: MockInterviewHistoryItem) => void;
}) {
  const [items, setItems] = useState<MockInterviewHistoryItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null);
  // Bumped whenever the first page reloads, so a Load more response that
  // straddles a refresh cannot append a stale page (duplicate rows).
  const generationRef = useRef(0);
  // True while the component is mounted; guards setState after unmount in
  // the async loadMore flow.
  const isMountedRef = useRef(true);

  // First page (or a refresh after a new interview): replace the list and
  // work out whether more pages exist - a full page means there probably is.
  useEffect(() => {
    const generation = ++generationRef.current;
    let cancelled = false;
    api
      .mockHistory(1, PAGE_SIZE)
      .then((res) => {
        if (cancelled || generation !== generationRef.current) return;
        const rows = res.interviews ?? [];
        setItems(rows);
        setPage(1);
        setHasMore(rows.length === PAGE_SIZE);
        setError(null);
      })
      .catch(() => {
        if (cancelled || generation !== generationRef.current) return;
        setItems([]);
        setError("Could not load your interview history.");
      });
    return () => {
      cancelled = true;
    };
  }, [attempt, refreshKey]);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  /**
   * Appends the next page to the list; without it everything past the
   * newest 20 interviews would be unreachable (CodeRabbit #176).
   */
  const loadMore = async () => {
    if (loadingMore) return;
    const generation = generationRef.current;
    setLoadingMore(true);
    setLoadMoreError(null);
    try {
      const res = await api.mockHistory(page + 1, PAGE_SIZE);
      if (!isMountedRef.current || generation !== generationRef.current) return;
      const rows = res.interviews ?? [];
      setItems((prev) => [...(prev ?? []), ...rows]);
      setPage((p) => p + 1);
      setHasMore(rows.length === PAGE_SIZE);
    } catch {
      if (!isMountedRef.current) return;
      setLoadMoreError("Could not load more interviews.");
      // keep what is already listed; the button stays for a retry
    } finally {
      if (!isMountedRef.current) return;
      setLoadingMore(false);
    }
  };

  if (error) {
    return <FetchError message={error} onRetry={() => setAttempt((n) => n + 1)} />;
  }

  if (items === null) {
    return (
      <div className="card !p-5 flex flex-col gap-2">
        {Array.from({ length: 3 }, (_, i) => (
          <div key={i} className="h-12 bg-border rounded-xl animate-pulse" />
        ))}
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div className="card !p-5">
        <p className="text-sm text-text-muted">
          No interviews yet. Start one and it will show up here.
        </p>
      </div>
    );
  }

  return (
    <>
      <ul className="flex flex-col gap-2">
        {items.map((item) => {
        const selected = item.interviewId === selectedId;
        return (
          <li key={item.interviewId}>
            <button
              type="button"
              onClick={() => onSelect?.(item)}
              disabled={!onSelect}
              className={`w-full text-left card-flat !p-4 transition-colors cursor-pointer ${
                selected
                  ? "!border-primary bg-accent-light-blue"
                  : "hover:bg-bg-light"
              } disabled:cursor-default disabled:hover:bg-transparent`}
            >
              <div className="flex items-center justify-between gap-3">
                <span className="text-sm font-medium text-navy">
                  {item.type} interview
                </span>
                <span className="text-xs text-text-muted">
                  {formatDate(item.date)}
                </span>
              </div>
              <div className="flex items-center gap-3 mt-1">
                <span className="text-xs text-text-muted">{item.status}</span>
                {item.score != null && (
                  <span className="text-xs font-semibold text-navy">
                    Score {Math.round(item.score)}
                  </span>
                )}
              </div>
            </button>
          </li>
        );
      })}
      </ul>
      {hasMore && (
        <>
          <button
            type="button"
            onClick={loadMore}
            disabled={loadingMore}
            className="w-full px-4 py-2 text-sm font-medium text-primary border border-border rounded-xl hover:bg-accent-light-blue transition-colors cursor-pointer disabled:opacity-60"
          >
            {loadingMore ? "Loading…" : "Load more"}
          </button>
          {loadMoreError && (
            <p className="text-xs text-danger mt-1">
              {loadMoreError}
            </p>
          )}
        </>
      )}
    </>
  );
}
