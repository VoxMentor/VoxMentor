"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, ApiError } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";

export default function AdminTutorSessionPage({
  params,
}: {
  params: Promise<{ sessionId: string }>;
}) {
  const [sessionId, setSessionId] = useState<string | null>(null);

  useEffect(() => {
    params.then((p) => setSessionId(p.sessionId));
  }, [params]);

  const [session, setSession] = useState<{
    id: string;
    userId: string;
    userEmail: string | null;
    conceptId: string | null;
    question: string;
    answer: string | null;
    status: string;
    totalTokens: number;
    createdAt: string;
    completedAt: string | null;
  } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadSession = useCallback(
    (signal?: AbortSignal) => {
      if (!sessionId) return;
      setLoading(true);
      setError(null);
      api
        .adminTutorSession(sessionId)
        .then((result) => {
          if (!signal?.aborted) {
            setSession(result);
          }
        })
        .catch((e) => {
          if (!signal?.aborted) {
            setError(e instanceof ApiError ? e.message : "Failed to load session.");
          }
        })
        .finally(() => {
          if (!signal?.aborted) {
            setLoading(false);
          }
        });
    },
    [sessionId]
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial data fetch
    loadSession(controller.signal);
    return () => controller.abort();
  }, [sessionId, loadSession]);

  if (loading) {
    return (
      <ProtectedRoute>
        <div className="min-h-screen bg-bg-light flex items-center justify-center">
          <div className="h-8 w-24 bg-border rounded animate-pulse" />
        </div>
      </ProtectedRoute>
    );
  }

  if (error || !session) {
    return (
      <ProtectedRoute>
        <div className="min-h-screen bg-bg-light">
          <Navbar />
          <div className="max-w-6xl mx-auto px-6 py-10">
            <div className="mb-4">
              <Link
                href="/admin/tutor-sessions"
                className="text-xs text-primary hover:text-primary-dark underline underline-offset-4"
              >
                ← Back to sessions
              </Link>
            </div>
            <FetchError message={error || "Session not found."} onRetry={loadSession} />
          </div>
        </div>
      </ProtectedRoute>
    );
  }

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="max-w-6xl mx-auto px-6 py-10">
          <div className="mb-8">
            <div className="flex items-center justify-between">
              <div>
                <Link
                  href="/admin/tutor-sessions"
                  className="text-xs text-primary hover:text-primary-dark underline underline-offset-4"
                >
                  ← Back to sessions
                </Link>
              </div>
              <div>
                <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
                  AI Tutor
                </h1>
                <p className="text-text-muted mt-2">
                  Session: {session.id} &nbsp;•&nbsp; User: {session.userEmail || session.userId}
                </p>
              </div>
            </div>
          </div>

          <div className="flex flex-col gap-6">
            {session.question && (
              <div className="card p-4 mb-4 bg-bg-light border-l-4 border-primary">
                <div className="flex items-start gap-3">
                  <span className="text-xs font-medium text-primary">Question</span>
                </div>
                <div className="p-3 rounded-lg bg-white border border-border text-text-body leading-relaxed whitespace-pre-wrap mt-2">
                  {session.question}
                </div>
              </div>
            )}
            {session.answer && (
              <div className="card p-4 mb-4 bg-bg-light border-l-4 border-green-500">
                <div className="flex items-start gap-3">
                  <span className="text-xs font-medium text-green-500">Answer</span>
                </div>
                <div className="p-3 rounded-lg bg-white border border-border text-text-body leading-relaxed whitespace-pre-wrap mt-2">
                  {session.answer}
                </div>
              </div>
            )}
            <div className="card p-4">
              <div className="flex items-center gap-3 mb-4 flex-wrap">
                <span className="text-xs font-medium text-text-muted">
                  Status: <span className="text-text-body capitalize">{session.status.toLowerCase()}</span>
                </span>
                <span className="text-xs font-medium text-text-muted">
                  Tokens: <span className="text-text-body">{session.totalTokens}</span>
                </span>
                <span className="text-xs font-medium text-text-muted">
                  Created: <span className="text-text-body">{new Date(session.createdAt).toLocaleString()}</span>
                </span>
                {session.completedAt && (
                  <span className="text-xs font-medium text-text-muted">
                    Completed: <span className="text-text-body">{new Date(session.completedAt).toLocaleString()}</span>
                  </span>
                )}
              </div>
            </div>
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}