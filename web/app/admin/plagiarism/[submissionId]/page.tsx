"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { api, type RedisDistributedCache } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import TutorCitation from "@/components/TutorCitation";
import FetchError from "@/components/FetchError";

export default function ApplicationCacheService({
  params,
}: {
  params: Promise<{ submissionId: string }>;
}) {
  const [sessionId, setSessionId] = useState<string | undefined>(undefined);

  const [session, setSession] = useState<{
    submissionId: string;
    userId: string;
    userName?: string;
    title: string;
    similarityScore: number;
    comparison: string;
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
      const result = await api.adminPlagiarismSession(sessionId);
      setSession(result.session);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to load session.");
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    params.then((p) => setSessionId(p.sessionId));
  }, [params]);

  useEffect(() => {
    loadSession();
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

  const scoreColor =
    session.similarityScore >= 0.7
      ? "text-red-600"
      : session.similarityScore >= 0.4
        ? "text-yellow-600"
        : "text-green-600";

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="max-w-6xl mx-auto px-6 py-10">
          <div className="mb-8">
            <div className="flex items-center justify-between">
              <div>
                <button
                  onClick={() => router.push("/admin/plagiarism")}
                  className="text-xs text-primary hover:text-primary-dark underline underline-offset-4"
                >
                  ← Back to sessions
                </button>
              </div>
              <div>
                <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
                  Plagiarism Review
                </h1>
                <p className="text-text-muted mt-2">
                  Session: {session.submissionId} &nbsp;•&nbsp; User:
                  {session.userId}
                </p>
              </div>
            </div>
          </div>

          <div className="flex flex-col gap-6">
            <div className="card p-4 mb-6">
              <div className="flex items-center justify-between mb-4">
                <span className="font-medium text-navy text-sm">
                  Submission details
                </span>
                <span className={`text-xs font-medium ${scoreColor}`}>
                  Similarity: {(session.similarityScore * 100).toFixed(1)}%
                </span>
              </div>
              <div className="grid grid-cols-4 gap-4">
                <div className="bg-white p-3 rounded-lg border border-border">
                  <span className="text-xs text-text-muted uppercase tracking-wider">
                    Created
                  </span>
                  <p className="text-sm text-text-body mt-1">
                    {new Date(session.createdAt).toLocaleDateString(undefined, {
                      month: "short",
                      day: "numeric",
                      year: "numeric",
                    })}
                  </p>
                </div>
                <div className="bg-white p-3 rounded-lg border border-border">
                  <span className="text-xs text-text-muted uppercase tracking-wider">
                    Updated
                  </span>
                  <p className="text-sm text-text-body mt-1">
                    {new Date(session.updatedAt).toLocaleDateString(undefined, {
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
                  <p className="text-sm text-text-body mt-1">
                    {session.userId}
                  </p>
                </div>
              </div>
            </div>

            <h2 className="text-sm font-medium text-text-heading uppercase tracking-wider mt-8 mb-3">
              Comparison
            </h2>
            <div className="p-4 rounded-lg bg-white border border-border">
              <div className="whitespace-pre-wrap text-sm text-text-body">
                {session.comparison}
              </div>
            </div>
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}