"use client";

import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { api, type TutorSessionSummaryDto } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import TutorChat from "@/components/TutorChat";
import FetchError from "@/components/FetchError";

function TutorPage() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();

  const [sessions, setSessions] = useState<TutorSessionSummaryDto[]>([]);
  const [sessionsLoading, setSessionsLoading] = useState(false);
  const [sessionsError, setSessionsError] = useState<string | null>(null);
  const [activeSessionId, setActiveSessionId] = useState<string | undefined>(
    searchParams.get("session") || undefined
  );
  // Auto-select the first session only once, on initial load — so "New Session"
  // can actually clear the active session instead of being immediately re-selected.
  const initialLoad = useRef(true);

  const loadSessions = useCallback(async () => {
    setSessionsLoading(true);
    setSessionsError(null);
    try {
      const sessionsData = await api.tutorSessions();
      setSessions(sessionsData);
    } catch (e) {
      setSessionsError(e instanceof Error ? e.message : "Failed to load sessions.");
    } finally {
      setSessionsLoading(false);
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial data fetch
    loadSessions();
  }, [loadSessions]);

  useEffect(() => {
    if (initialLoad.current && !activeSessionId && sessions.length > 0) {
      initialLoad.current = false;
      setActiveSessionId(sessions[0].sessionId);
      const q = new URLSearchParams(searchParams.toString());
      q.set("session", sessions[0].sessionId);
      router.replace(`${pathname}?${q.toString()}`);
    }
  }, [sessions, activeSessionId, searchParams, router, pathname]);

  const handleSessionSelect = (sessionId: string) => {
    setActiveSessionId(sessionId);
    const q = new URLSearchParams(searchParams.toString());
    q.set("session", sessionId);
    router.replace(`${pathname}?${q.toString()}`);
  };

  const handleNewSession = () => {
    setActiveSessionId(undefined);
    const q = new URLSearchParams(searchParams.toString());
    q.delete("session");
    router.replace(`${pathname}?${q.toString()}`);
  };

  const sessionsList = sessions.length === 0 ? (
    <p className="text-xs text-text-muted text-center py-4">
      No sessions yet. Start by asking a question.
    </p>
  ) : (
      sessions.map((session) => (
        <button
          key={session.sessionId}
          onClick={() => handleSessionSelect(session.sessionId)}
          className={`w-full text-left px-3 py-2.5 text-sm text-text-body border-[1.5px] border-border rounded-[10px] hover:bg-accent-light-blue transition-colors cursor-pointer`}
        >
          <div className="flex justify-between items-start mb-1">
            <span className="font-medium text-text-heading truncate">
              {session.question}
            </span>
            <span className="text-[10px] text-text-muted">
              {formatDate(session.createdAt)}
            </span>
          </div>
        </button>
      ))
    );

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />

        <main className="max-w-6xl mx-auto px-6 py-10">
          <div className="mb-8">
            <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
              AI Tutor
            </h1>
            <p className="text-text-muted mt-2 max-w-2xl">
              Ask questions about DSA concepts and get step-by-step guidance.
            </p>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-[280px_1fr] gap-6 min-h-[600px]">
            <aside className="flex flex-col gap-4 min-w-0">
              <div className="flex items-center justify-between mb-4">
                <span className="font-medium text-navy text-sm">Sessions</span>
                <button
                  onClick={handleNewSession}
                  className="text-xs text-primary hover:text-primary-dark underline underline-offset-4"
                >
                  New Session
                </button>
              </div>

              {sessionsLoading && (
                <div className="h-40 flex items-center justify-center">
                  <div className="h-8 w-24 bg-border rounded animate-pulse" />
                </div>
              )}

              {sessionsError && (
                <FetchError message={sessionsError} onRetry={loadSessions} />
              )}

              {!sessionsLoading && !sessionsError && (
                <div className="flex-1 overflow-y-auto space-y-1">
                  {sessionsList}
                </div>
              )}
            </aside>

            <div className="flex flex-col min-w-0">
              <TutorChat
                sessionId={activeSessionId}
                onSessionChange={(id) => {
                  setActiveSessionId(id === null ? undefined : id);
                  const q = new URLSearchParams(searchParams.toString());
                  if (id) q.set("session", id);
                  else q.delete("session");
                  router.replace(`${pathname}?${q.toString()}`);
                  // Refresh the sidebar so a newly created session appears,
                  // and re-apply the session query param after a reload.
                  loadSessions();
                }}
              />
            </div>
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}

function formatDate(iso: string) {
  const d = new Date(iso);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  if (diffMins < 1) return "just now";
  if (diffMins < 60) return `${diffMins}m ago`;
  if (diffMins < 1440) return `${Math.floor(diffMins / 60)}h ago`;
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

export default function TutorPageWrapper() {
  return (
    <Suspense fallback={<div>Loading...</div>}>
      <TutorPage />
    </Suspense>
  );
}
