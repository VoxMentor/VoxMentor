"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { api, ApiError, type TutorMessage } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import TutorCitation from "@/components/TutorCitation";
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
    sessionId: string;
    userId: string;
    userName?: string;
    messages: TutorMessage[];
    createdAt: string;
    updatedAt: string;
  } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadSession = useCallback(async () => {
    if (!sessionId) return;
    setLoading(true);
    setError(null);
    try {
      const result = await api.adminTutorSession(sessionId);
      setSession(result.session);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to load session.");
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    setTimeout(() => {
      loadSession();
    }, 0);
  }, [sessionId, loadSession]);

  const router = useRouter();

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
        <div className="max-w-6xl mx-auto px-6 py-10">
          <Navbar />
          <FetchError message={error || "Session not found."} onRetry={loadSession} />
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
                <button
                  onClick={() => router.push("/admin/tutor-sessions")}
                  className="text-xs text-primary hover:text-primary-dark underline underline-offset-4"
                >
                  ← Back to sessions
                </button>
              </div>
              <div>
                <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
                  AI Tutor
                </h1>
                <p className="text-text-muted mt-2">
                  Session: {session.sessionId} &nbsp;•&nbsp; User:
                  {session.userId}
                </p>
              </div>
            </div>
          </div>

          <div className="flex flex-col gap-6">
            {session.messages.map((msg) => (
              <div key={msg.id} className="card p-4 mb-4">
                <div className="flex items-start gap-3">
                  <span
                    className={`text-xs font-medium ${
                      msg.role === "user"
                        ? "text-primary"
                        : "text-text-muted"
                    }`}
                  >
                    {msg.role.charAt(0).toUpperCase() + msg.role.slice(1)}
                  </span>
                </div>
                <div className="p-3 rounded-lg bg-white border border-border text-text-body leading-relaxed whitespace-pre-wrap">
                  {msg.content}
                </div>
                {msg.citations && msg.citations.length > 0 && (
                  <div className="mt-3">
                    <TutorCitation citations={msg.citations} />
                  </div>
                )}
              </div>
            ))}
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}