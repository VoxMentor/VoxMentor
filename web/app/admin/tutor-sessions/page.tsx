"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuth } from "@/lib/auth";
import { api, ApiError, type AdminTutorSession } from "@/lib/api";
import FetchError from "@/components/FetchError";

/**
 * Admin view: lists tutor sessions across the platform.
 * Only PlatformAdmin / SuperAdmin can access this page.
 */
export default function AdminTutorSessionsPage() {
  const { user } = useAuth();
  const router = useRouter();

  // Role check: only PlatformAdmin / SuperAdmin can access
  const isPlatformAdmin =
    !!user?.roles?.includes("PlatformAdmin") || !!user?.roles?.includes("SuperAdmin");

  // Redirect non-platform admins (but still call hooks first)
  useEffect(() => {
    if (user && !isPlatformAdmin) {
      router.replace("/admin");
    }
  }, [user, isPlatformAdmin, router]);

  const [sessions, setSessions] = useState<AdminTutorSession[]>([]);
  const [sessionsLoading, setSessionsLoading] = useState(true);
  const [sessionsError, setSessionsError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);

  const loadSessions = useCallback(
    (signal?: AbortSignal) => {
      setSessionsLoading(true);
      setSessionsError(null);
      api
        .adminTutorSessions(page, pageSize)
        .then((result) => {
          if (!signal?.aborted) {
            setSessions(result);
            // Calculate total pages
            if (result.length < pageSize) {
              setTotalPages(page);
            } else {
              setTotalPages((prev) => Math.max(prev, page + 1));
            }
            setSessionsLoading(false);
          }
        })
        .catch((e) => {
          if (!signal?.aborted) {
            setSessionsError(e instanceof ApiError ? e.message : "Failed to load sessions.");
            setSessionsLoading(false);
          }
        });
    },
    [page, pageSize]
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial data fetch
    loadSessions(controller.signal);
    return () => controller.abort();
  }, [loadSessions]);

  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPage(newPage);
    }
  };

  const handleRetry = () => {
    const controller = new AbortController();
    loadSessions(controller.signal);
  };

  // Early return for unauthorized users (after hooks)
  if (!user || !isPlatformAdmin) return null;

  return (
    <>
      <header className="mb-8">
        <h1 className="font-heading text-2xl font-semibold text-navy">
          Tutor sessions
        </h1>
        <p className="mt-1 max-w-2xl text-sm text-text-muted">
          Tutor chat sessions across the platform.
        </p>
      </header>

      {sessionsLoading && (
        <div className="panel" aria-hidden="true">
          <div className="h-4 w-24 bg-border rounded animate-pulse" />
        </div>
      )}

      {sessionsError && (
        <FetchError message={sessionsError} onRetry={handleRetry} />
      )}

      {!sessionsLoading && !sessionsError && (
        <div className="flex flex-col gap-4">
          {sessions.length === 0 ? (
            <div className="panel">
              <p className="text-sm text-text-muted">No tutor sessions yet.</p>
            </div>
          ) : (
            <div className="panel overflow-hidden">
              <table className="w-full text-left border-collapse">
                <caption className="sr-only">Tutor sessions across the platform</caption>
                <thead>
                  <tr className="border-b border-border">
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">Session</th>
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">Title</th>
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">User</th>
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">Status</th>
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">Created</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {sessions.map((session) => (
                    <tr key={session.id} className="transition-colors hover:bg-bg-light">
                      <td className="px-4 py-3 font-mono text-xs tabular-nums text-text-muted">
                        {session.id}
                      </td>
                      <td className="px-4 py-3 text-sm text-navy">
                        <Link
                          href={`/admin/tutor-sessions/${session.id}`}
                          className="underline underline-offset-2 transition-colors hover:text-primary-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy"
                        >
                          {session.question || "Untitled session"}
                        </Link>
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body truncate max-w-[200px]">
                        {session.userEmail || session.userId}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body">
                        {session.status}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-muted tabular-nums">
                        {new Date(session.createdAt).toLocaleDateString(
                          undefined,
                          { month: "short", day: "numeric", year: "numeric" }
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {/* Pagination controls — render even when empty so users can navigate back */}
          {totalPages > 1 && (
            <nav className="flex items-center justify-between mt-1 px-1" aria-label="Pagination">
              <button
                onClick={() => handlePageChange(page - 1)}
                disabled={page === 1}
                className="px-3 py-1.5 text-sm text-navy rounded-[10px] border border-border cursor-pointer transition-colors hover:bg-bg-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
              >
                Previous
              </button>
              <span className="font-mono text-xs tabular-nums text-text-muted">
                Page {page} of {totalPages}
              </span>
              <button
                onClick={() => handlePageChange(page + 1)}
                disabled={page === totalPages}
                className="px-3 py-1.5 text-sm text-navy rounded-[10px] border border-border cursor-pointer transition-colors hover:bg-bg-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
              >
                Next
              </button>
            </nav>
          )}
        </div>
      )}
    </>
  );
}
