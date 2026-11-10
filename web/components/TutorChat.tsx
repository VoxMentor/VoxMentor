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
  sessionId: initialSessionId,
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

  // Session state
  const [sessionId, setSessionId] = useState<string | null>(initialSessionId ?? null);

  // Messages state
  const [messages, setMessages] = useState<{ id: string; role: string; content: string; citations?: Citation[] }[]>([]);

  // Concept selector state
  const [concepts, setConcepts] = useState<{ conceptId: string; name: string }[]>([]);
  const [selectedConcept, setSelectedConcept] = useState<string | null>(null);

  const statusRef = useRef<Status>("idle");

  // Keep statusRef in sync with status for the onclose handler
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

  // Load session detail when sessionId changes
  useEffect(() => {
    if (!sessionId) {
      setTimeout(() => {
        setMessages([]);
        setAnswer("");
        setError("");
        setStats(null);
      }, 0);
      return;
    }

    api
      .tutorSession(sessionId)
      .then((result) => {
        setMessages(result.session.messages);
        setAnswer("");
        setError("");
        setStats(null);
      })
      .catch(() => {
        setError("Failed to load session.");
      });
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

    try {
      const conn = getTutorConnection();

      conn.off("TutorToken");
      conn.off("TutorComplete");
      conn.off("TutorError");

      conn.on("TutorToken", (token: string) => {
        setStatus("streaming");
        setAnswer((prev) => prev + token);
      });
      conn.on("TutorComplete", (payload: TutorCompletePayload) => {
        setStats({ sessionId: payload.sessionId, totalTokens: payload.totalTokens });

        if (payload.sessionId && payload.sessionId !== sessionId) {
          setSessionId(payload.sessionId);
          onSessionChange?.(payload.sessionId);
        }

        setMessages((prev) => [
          ...prev,
          {
            id: Date.now().toString(),
            role: "assistant",
            content: answer,
            citations: payload.citations,
          },
        ]);

        setStatus("done");
      });
      conn.on("TutorError", (payload: TutorErrorPayload) => {
        setError(payload.message);
        setStatus("error");
      });

      if (conn.state !== "Connected") {
        await conn.start();
      }
      await conn.invoke("AskTutor", sessionId ?? "", q);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setStatus("error");
    }
  }

  const handleNewSession = () => {
    setSessionId(null);
    setTimeout(() => {
      setMessages([]);
      setAnswer("");
      setError("");
      setStats(null);
    }, 0);
    setQuestion("");
    onSessionChange?.(null);
  };

  return (
    <div className="card flex flex-col h-full">
      <div className="flex items-center justify-between mb-3">
        <h2 className="font-heading font-semibold text-navy">Ask the AI Tutor</h2>
        <div className="flex items-center gap-2">
          {sessionId && (
            <button
              onClick={handleNewSession}
              className="text-xs text-text-muted hover:text-primary transition-colors"
            >
              New Session
            </button>
          )}
          {status === "done" && stats && (
            <span className="text-xs text-text-muted">
              {stats.totalTokens} tokens
            </span>
          )}
        </div>
      </div>

      {/* Concept selector */}
      <div className="mb-3">
        <label htmlFor="concept-selector" className="block text-xs font-medium text-text-muted mb-1">
          Concept
        </label>
        <select
          id="concept-selector"
          value={selectedConcept ?? ""}
          onChange={(e) => setSelectedConcept(e.target.value || null)}
          className="w-full text-sm rounded-[14px] border-[1.5px] border-border bg-white px-4 py-3 text-text-body focus:border-primary focus:shadow-[0_0_0_3px_rgba(74,173,219,0.12)] focus:outline-none cursor-pointer disabled:opacity-60"
          disabled={busy}
        >
          <option value="">All concepts</option>
          {concepts.map((c) => (
            <option key={c.conceptId} value={c.conceptId}>
              {c.name}
            </option>
          ))}
        </select>
      </div>

      <textarea
        value={question}
        onChange={(e) => setQuestion(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            ask();
          }
        }}
        placeholder="Ask a concept question… (Enter to send, Shift+Enter for newline)"
        rows={3}
        disabled={busy}
        className="w-full p-3 text-sm text-text-heading bg-white border-[1.5px] border-[var(--border)] rounded-[14px] outline-none focus:border-primary transition-all resize-none disabled:opacity-60"
      />

      <div className="flex items-center gap-3 mt-3">
        <button onClick={ask} disabled={busy || question.trim().length === 0} className="btn-primary">
          {busy ? (status === "connecting" ? "Connecting…" : "Thinking…") : "Ask"}
        </button>
        {status === "streaming" && (
          <span className="text-xs text-primary animate-pulse">streaming…</span>
        )}
        {error && <span className="text-xs text-red-500">{error}</span>}
      </div>

      {(answer || status === "streaming") && (
        <div className="mt-4 p-4 rounded-2xl bg-bg-light text-sm leading-relaxed text-text-heading whitespace-pre-wrap min-h-[3rem]">
          {answer}
          {status === "streaming" && <span className="animate-pulse">▍</span>}
        </div>
      )}

      {stats?.citations && stats.citations.length > 0 && (
        <TutorCitation citations={stats.citations} />
      )}
    </div>
  );
}
