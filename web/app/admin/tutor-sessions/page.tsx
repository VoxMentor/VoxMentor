"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuth } from "@/lib/auth";
import { api, ApiError, type AdminTutorSession } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
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
      router.replace("/dashboard");
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
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="max-w-6xl mx-auto px-6 py-10">
          <div className="mb-8">
            <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
              Tutor Sessions
            </h1>
            <p className="text-text-muted mt-2 max-w-2xl">
              View tutor chat sessions across the platform.
            </p>
          </div>

          {sessionsLoading && (
            <div className="card p-4 flex items-center justify-center">
              <div className="h-8 w-24 bg-border rounded animate-pulse" />
            </div>
          )}

          {sessionsError && (
            <FetchError message={sessionsError} onRetry={handleRetry} />
          )}

          {!sessionsLoading && !sessionsError && (
            <div className="flex flex-col gap-4">
              {sessions.length === 0 ? (
                <div className="card p-8 text-center text-text-muted">
                  No tutor sessions yet.
                </div>
              ) : (
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-border text-left">
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        Session
                      </th>
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        Title
                      </th>
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        User
                      </th>
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        Status
                      </th>
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        Created
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {sessions.map((session) => (
                      <tr
                        key={session.id}
                        className="border-b border-border transition-colors hover:bg-accent-light-blue"
                      >
                        <td className="py-3 px-4 text-sm text-text-body">
                          {session.id}
                        </td>
                        <td className="py-3 px-4 text-sm text-text-body">
                          <Link
                            href={`/admin/tutor-sessions/${session.id}`}
                            className="text-primary hover:text-primary-dark underline"
                          >
                            {session.question || "Untitled session"}
                          </Link>
                        </td>
                        <td className="py-3 px-4 text-sm text-text-body">
                          {session.userEmail || session.userId}
                        </td>
                        <td className="py-3 px-4 text-sm text-text-body">
                          {session.status}
                        </td>
                        <td className="py-3 px-4 text-sm text-text-muted">
                          {new Date(session.createdAt).toLocaleDateString(
                            undefined,
                            { month: "short", day: "numeric", year: "numeric" }
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              {/* Pagination controls — render even when empty so users can navigate back */}
              {totalPages > 1 && (
                <div className="flex items-center justify-center gap-2 mt-4">
                  <button
                    onClick={() => handlePageChange(page - 1)}
                    disabled={page === 1}
                    className="px-3 py-1.5 text-sm text-text-body bg-white border border-border rounded disabled:opacity-50 disabled:cursor-not-allowed hover:bg-accent-light-blue transition-colors"
                  >
                    Previous
                  </button>
                  <span className="text-sm text-text-muted">
                    Page {page} of {totalPages}
                  </span>
                  <button
                    onClick={() => handlePageChange(page + 1)}
                    disabled={page === totalPages}
                    className="px-3 py-1.5 text-sm text-text-body bg-white border border-border rounded disabled:opacity-50 disabled:cursor-not-allowed hover:bg-accent-light-blue transition-colors"
                  >
                    Next
                  </button>
                </div>
              )}
            </div>
          )}
        </main>
      </div>
    </ProtectedRoute>
  );
}