"use client";

import { useEffect, useRef, useState } from "react";
import { getTutorConnection } from "@/lib/signalr";
import { api, type Citation } from "@/lib/api";
import type { HubConnection } from "@microsoft/signalr";
import TutorCitation from "@/components/TutorCitation";

type Status = "idle" | "connecting" | "streaming" | "done" | "error";

interface TutorCompletePayload {
  sessionId: string;
  totalTokens: number;
  citations?: Citation[];
}

interface TutorErrorPayload {
  sessionId: string | null;
  message: string;
}

// ponytail: one mounted TutorChat (dashboard). The close callback must be
// registered exactly once per cached connection — HubConnection.onclose has no
// matching off(), so re-registering per mount would pile up handlers.
let closeHandler: ((err?: Error) => void) | null = null;
const closeRegistered = new WeakSet<HubConnection>();

interface TutorChatProps {
  /** Optional session ID to load on mount. Omit for a new session. */
  sessionId?: string;
  /** Fired when the active session changes (new session created or selected). */
  onSessionChange?: (sessionId: string | null) => void;
}

export default function TutorChat({
  sessionId: sessionIdProp,
  onSessionChange,
}: TutorChatProps = {}) {
  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState("");
  const [status, setStatus] = useState<Status>("idle");
  const [error, setError] = useState("");
  const [stats, setStats] = useState<{
    sessionId: string;
    totalTokens: number;
    citations?: Citation[];
  } | null>(null);

  // Session state — derived from the prop so selecting a session in the
  // sidebar loads its history (prop is not read once at mount).
  const [sessionId, setSessionId] = useState<string | null>(sessionIdProp ?? null);
  // Sync internal sessionId with prop changes (controlled component pattern).
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- intentional sync
    setSessionId(sessionIdProp ?? null);
  }, [sessionIdProp]);

  // Latest answer, so the TutorComplete handler (registered inside ask())
  // doesn't close over a stale `answer` ("").
  const answerRef = useRef("");
  useEffect(() => {
    answerRef.current = answer;
  }, [answer]);

  // Concept selector state
  const [concepts, setConcepts] = useState<{ conceptId: string; name: string }[]>([]);
  const [selectedConcept, setSelectedConcept] = useState<string | null>(null);

  // Session ID this client just created — skip the detail reload for it so the
  // streamed answer/citations aren't wiped by a server poll. `undefined` means
  // "none pending" so a null sessionId (sidebar New Session) doesn't match it.
  const justCreatedRef = useRef<string | undefined>(undefined);

  // The sessionId the current token stream belongs to. Tokens arriving for a
  // different session (e.g. after a mid-stream session change) are ignored so
  // stream events stay scoped to their session (#172).
  const streamSessionRef = useRef<string | null>(null);

  const statusRef = useRef<Status>("idle");
  useEffect(() => {
    statusRef.current = status;
  }, [status]);

  // Fetch concepts on mount for the concept selector
  useEffect(() => {
    api
      .mastery()
      .then((profile) => {
        setConcepts(
          profile.concepts.map((c) => ({
            conceptId: c.conceptId,
            name: c.name,
          }))
        );
      })
      .catch(() => {});
  }, []);

  // Load session detail when sessionId changes, with cancellation so an
  // obsolete response can't overwrite a newer session's state.
  useEffect(() => {
    // Skip reload for a session this client just created.
    if (sessionId === justCreatedRef.current) {
      justCreatedRef.current = undefined;
      return;
    }
    if (!sessionId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- clearing state on session clear
      setAnswer("");
      setError("");
      setStats(null);
      return;
    }

    let cancelled = false;
    api
      .tutorSession(sessionId)
      .then((result) => {
        if (cancelled) return;
        // Restore the stored Q&A for the selected session so clicking a session
        // in the sidebar loads its prior question and answer (acceptance criteria).
        setAnswer(result.answer ?? "");
        setError("");
        setStats({
          sessionId,
          totalTokens: result.totalTokens,
          // Historical sessions don't carry per-chunk citations; they arrive via
          // the SignalR TutorComplete event during streaming.
          citations: undefined,
        });
      })
      .catch(() => {
        if (!cancelled) setError("Failed to load session.");
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  useEffect(() => {
    const conn = getTutorConnection();

    closeHandler = (err) => {
      if (statusRef.current === "streaming" || statusRef.current === "connecting") {
        setError(err?.message ?? "Connection lost");
        setStatus("error");
      }
    };
    if (!closeRegistered.has(conn)) {
      closeRegistered.add(conn);
      conn.onclose((err) => closeHandler?.(err));
    }

    return () => {
      closeHandler = null;
      conn.off("TutorToken");
      conn.off("TutorComplete");
      conn.off("TutorError");
    };
  }, []);

  const busy = status === "connecting" || status === "streaming";

  async function ask() {
    const q = question.trim();
    if (!q || busy) return;

    setStatus("connecting");
    setError("");
    setAnswer("");
    setStats(null);
    // Scope this token stream to the current session so tokens never bleed
    // into another session's answer.
    streamSessionRef.current = sessionId;

    try {
      const conn = getTutorConnection();

      conn.off("TutorToken");
      conn.off("TutorComplete");
      conn.off("TutorError");

      conn.on("TutorToken", (token: string) => {
        // Ignore tokens that don't belong to the current session. A null
        // sessionId means a brand-new session is being created, so tokens are
        // allowed until TutorComplete assigns the real session id.
        if (sessionId !== null && sessionId !== streamSessionRef.current) return;
        setStatus("streaming");
        setAnswer((prev) => prev + token);
      });
      conn.on("TutorComplete", (payload: TutorCompletePayload) => {
        setStats({
          sessionId: payload.sessionId,
          totalTokens: payload.totalTokens,
          citations: payload.citations,
        });

        if (payload.sessionId && payload.sessionId !== sessionId) {
          justCreatedRef.current = payload.sessionId;
          setSessionId(payload.sessionId);
          onSessionChange?.(payload.sessionId);
        }

        setStatus("done");
      });
      conn.on("TutorError", (payload: TutorErrorPayload) => {
        setError(payload.message);
        setStatus("error");
      });

      if (conn.state !== "Connected") {
        await conn.start();
      }
      // The hub signature is AskTutor(conceptId, question). Pass the selected
      // concept, not the session ID (a session ID is not a valid concept Guid).
      await conn.invoke("AskTutor", selectedConcept ?? "", q);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setStatus("error");
    }
  }

  const handleNewSession = () => {
    setSessionId(null);
    setAnswer("");
    setError("");
    setStats(null);
    setQuestion("");
    onSessionChange?.(null);
  };

  return (
    <div className="panel">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-heading text-base font-semibold text-navy">
            AI tutor
          </h2>
          <p className="mt-1 text-sm text-text-muted">
            Ask about any concept in your curriculum.
          </p>
        </div>
        <div className="flex items-center gap-3">
          {sessionId && (
            <button
              onClick={handleNewSession}
              className="text-sm font-medium text-text-muted transition-colors hover:text-navy"
            >
              New session
            </button>
          )}
          {status === "done" && stats && (
            <span className="text-xs text-text-muted">
              {stats.totalTokens} tokens
            </span>
          )}
        </div>
      </div>

      <div className="mt-5 grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Form */}
        <div>
          <label
            htmlFor="concept-selector"
            className="mb-1.5 block text-xs font-medium text-text-muted"
          >
            Concept
          </label>
          <select
            id="concept-selector"
            value={selectedConcept ?? ""}
            onChange={(e) => setSelectedConcept(e.target.value || null)}
            className="w-full cursor-pointer rounded-[10px] border border-border bg-white px-3 py-2 text-sm text-text-body focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/15 disabled:opacity-60"
            disabled={busy}
          >
            <option value="">All concepts</option>
            {concepts.map((c) => (
              <option key={c.conceptId} value={c.conceptId}>
                {c.name}
              </option>
            ))}
          </select>

          <label
            htmlFor="tutor-question"
            className="mt-4 mb-1.5 block text-xs font-medium text-text-muted"
          >
            Question
          </label>
          <textarea
            id="tutor-question"
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                ask();
              }
            }}
            placeholder="Ask about a DSA concept"
            rows={4}
            disabled={busy}
            className="w-full resize-none rounded-[10px] border border-border bg-white px-3 py-2 text-sm text-text-heading placeholder:text-text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/15 disabled:opacity-60"
          />
          <p className="mt-1.5 text-xs text-text-muted">
            Enter to send, Shift+Enter for a new line.
          </p>

          <div className="mt-3 flex items-center gap-3">
            <button
              onClick={ask}
              disabled={busy || question.trim().length === 0}
              className="btn-solid"
            >
              {busy ? (status === "connecting" ? "Connecting" : "Thinking") : "Ask"}
            </button>
            {status === "streaming" && (
              <span className="text-xs text-text-muted">Streaming</span>
            )}
          </div>

          {error && (
            <p role="alert" className="error-banner mt-3 !mb-0">
              {error}
            </p>
          )}
        </div>

        {/* Answer */}
        <div className="flex flex-col">
          <p className="mb-1.5 text-xs font-medium text-text-muted">Answer</p>
          <div className="min-h-[12rem] flex-1 whitespace-pre-wrap rounded-[10px] border border-border bg-bg-light p-4 text-sm leading-relaxed text-text-heading">
            {answer || status === "streaming" ? (
              <>
                {answer}
                {status === "streaming" && (
                  <span aria-hidden="true" className="text-primary">
                    ▍
                  </span>
                )}
              </>
            ) : (
              <span className="text-text-muted">Answers appear here.</span>
            )}
          </div>

          {stats?.citations && stats.citations.length > 0 && (
            <TutorCitation citations={stats.citations} />
          )}
        </div>
      </div>
    </div>
  );
}