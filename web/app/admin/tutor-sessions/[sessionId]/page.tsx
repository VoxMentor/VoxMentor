"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { api, ApiError } from "@/lib/api";
import FetchError from "@/components/FetchError";

export default function AdminTutorSessionPage({
  params,
}: {
  params: Promise<{ sessionId: string }>;
}) {
  const { user } = useAuth();
  const router = useRouter();
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [sessionIdResolved, setSessionIdResolved] = useState(false);

  // Role check: only PlatformAdmin / SuperAdmin can access
  const isPlatformAdmin =
    !!user?.roles?.includes("PlatformAdmin") || !!user?.roles?.includes("SuperAdmin");

  // Redirect non-platform admins (but still call hooks first)
  useEffect(() => {
    if (user && !isPlatformAdmin) {
      router.replace("/admin");
    }
  }, [user, isPlatformAdmin, router]);

  useEffect(() => {
    params
      .then((p) => {
        setSessionId(p.sessionId);
        setSessionIdResolved(true);
      })
      .catch(() => {
        setSessionIdResolved(true);
      });
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
    // Don't start loading until sessionId is resolved
    if (!sessionIdResolved) return;
    if (!sessionId) {
      // Use setTimeout to defer setState calls, avoiding the rule violation
      setTimeout(() => {
        setLoading(false);
        setError("Invalid session ID.");
      }, 0);
      return;
    }
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial data fetch
    loadSession(controller.signal);
    return () => controller.abort();
  }, [sessionId, sessionIdResolved, loadSession]);

  // Early return for unauthorized users (after hooks)
  if (!user || !isPlatformAdmin) return null;

  const backLink = (
    <Link
      href="/admin/tutor-sessions"
      className="inline-block text-xs text-navy underline underline-offset-4 transition-colors hover:text-primary-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy"
    >
      ← Back to sessions
    </Link>
  );

  if (loading) {
    return (
      <div className="panel" aria-hidden="true">
        <div className="h-4 w-24 bg-border rounded animate-pulse" />
      </div>
    );
  }

  if (error || !session) {
    return (
      <div>
        <div className="mb-4">{backLink}</div>
        <FetchError
          message={error || "Session not found."}
          onRetry={() => {
            const controller = new AbortController();
            loadSession(controller.signal);
          }}
        />
      </div>
    );
  }

  return (
    <div>
      <div className="mb-4">{backLink}</div>

      <header className="mb-8">
        <h1 className="font-heading text-2xl font-semibold text-navy">
          Tutor session
        </h1>
        <p className="mt-1 max-w-2xl text-sm text-text-muted">
          User {session.userEmail || session.userId}
        </p>
      </header>

      <div className="flex flex-col gap-6">
        {session.question && (
          <section>
            <h2 className="mb-3 text-sm font-medium text-navy">Question</h2>
            <div className="panel-sm whitespace-pre-wrap text-sm leading-relaxed text-text-body">
              {session.question}
            </div>
          </section>
        )}
        {session.answer && (
          <section>
            <h2 className="mb-3 text-sm font-medium text-navy">Answer</h2>
            <div className="panel-sm whitespace-pre-wrap text-sm leading-relaxed text-text-body">
              {session.answer}
            </div>
          </section>
        )}
        <section>
          <h2 className="mb-3 text-sm font-medium text-navy">Session details</h2>
          <dl className="panel grid grid-cols-1 gap-x-8 gap-y-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="min-w-0">
              <dt className="text-xs text-text-muted">Status</dt>
              <dd className="mt-1 text-sm text-text-body capitalize">
                {session.status.toLowerCase()}
              </dd>
            </div>
            <div className="min-w-0">
              <dt className="text-xs text-text-muted">Tokens</dt>
              <dd className="mt-1 text-sm text-text-body tabular-nums">
                {session.totalTokens}
              </dd>
            </div>
            <div className="min-w-0">
              <dt className="text-xs text-text-muted">Created</dt>
              <dd className="mt-1 text-sm text-text-body tabular-nums">
                {new Date(session.createdAt).toLocaleString()}
              </dd>
            </div>
            <div className="min-w-0">
              <dt className="text-xs text-text-muted">Completed</dt>
              <dd className="mt-1 text-sm text-text-body tabular-nums">
                {session.completedAt
                  ? new Date(session.completedAt).toLocaleString()
                  : "n/a"}
              </dd>
            </div>
          </dl>
        </section>
      </div>
    </div>
  );
}
