"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, ApiError, type AdminTutorSession } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";

/**
 * Admin view: lists tutor sessions across the platform.
 * Only PlatformAdmin / SuperAdmin can access this page (guarded by AdminRoute
 * and the nav item role filter).
 */
export default function AdminTutorSessionsPage() {
  const [sessions, setSessions] = useState<AdminTutorSession[]>([]);
  const [sessionsLoading, setSessionsLoading] = useState(true);
  const [sessionsError, setSessionsError] = useState<string | null>(null);

  const loadSessions = useCallback(
    (signal?: AbortSignal) => {
      setSessionsLoading(true);
      setSessionsError(null);
      api
        .adminTutorSessions()
        .then((result) => {
          if (!signal?.aborted) {
            setSessions(result);
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
    []
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial data fetch
    loadSessions(controller.signal);
    return () => controller.abort();
  }, [loadSessions]);

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
            <FetchError
              message={sessionsError}
              onRetry={() => {
                const controller = new AbortController();
                loadSessions(controller.signal);
              }}
            />
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
                        Updated
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {sessions.map((session) => (
                      <tr
                        key={session.id}
                        className="border-b border-border cursor-pointer transition-colors hover:bg-accent-light-blue"
                      >
                        <td className="py-3 px-4 text-sm text-text-body">
                          {session.id}
                        </td>
                        <td className="py-3 px-4 text-sm text-text-body">
                          <Link
                            href={`/admin/tutor-sessions/${session.id}`}
                            className="text-primary hover:text-primary-dark underline"
                            onClick={(e) => e.stopPropagation()}
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
            </div>
          )}
        </main>
      </div>
    </ProtectedRoute>
  );
}