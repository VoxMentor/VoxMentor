"use client";

import { useEffect, useRef, useState } from "react";
import { getTutorConnection } from "@/lib/signalr";
import type { HubConnection } from "@microsoft/signalr";

interface HubConnectionWithOffClose extends HubConnection {
  offclose: (callback: (err?: Error) => void) => void;
}

type Status = "idle" | "connecting" | "streaming" | "done" | "error";

interface TutorCompletePayload {
  sessionId: string;
  totalTokens: number;
}

interface TutorErrorPayload {
  sessionId: string | null;
  message: string;
}

export default function TutorChat() {
  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState("");
  const [status, setStatus] = useState<Status>("idle");
  const [error, setError] = useState("");
  const [stats, setStats] = useState<{ sessionId: string; totalTokens: number } | null>(null);

  const statusRef = useRef<Status>("idle");
  const connRef = useRef<ReturnType<typeof getTutorConnection> | null>(null);
  const onCloseRef = useRef<(err?: Error) => void>(() => {});

  // Keep statusRef in sync with status for the onclose handler
  useEffect(() => {
    statusRef.current = status;
  }, [status]);

  useEffect(() => {
    const conn = getTutorConnection();
    connRef.current = conn;

    const handleClose = (err?: Error) => {
      if (statusRef.current === "streaming" || statusRef.current === "connecting") {
        setError(err?.message ?? "Connection lost");
        setStatus("error");
      }
    };
    onCloseRef.current = handleClose;
    conn.onclose(handleClose);

    return () => {
      // Cleanup handlers on unmount to prevent memory leaks
      conn.off("TutorToken");
      conn.off("TutorComplete");
      conn.off("TutorError");
      if (onCloseRef.current) {
        (conn as HubConnectionWithOffClose).offclose(onCloseRef.current);
      }
    };
  }, []);

  const busy = status === "connecting" || status === "streaming";

  async function ask() {
    const q = question.trim();
    if (!q || busy) return;

    // Set busy immediately to prevent double-click race
    setStatus("connecting");
    setError("");
    setAnswer("");
    setStats(null);

    try {
      const conn = getTutorConnection();

      // Re-register handlers each ask so tokens never append twice.
      conn.off("TutorToken");
      conn.off("TutorComplete");
      conn.off("TutorError");

      conn.on("TutorToken", (token: string) => {
        setStatus("streaming");
        setAnswer((prev) => prev + token);
      });
      conn.on("TutorComplete", (payload: TutorCompletePayload) => {
        setStats(payload);
        setStatus("done");
      });
      conn.on("TutorError", (payload: TutorErrorPayload) => {
        setError(payload.message);
        setStatus("error");
      });

      if (conn.state !== "Connected") {
        await conn.start();
      }
      await conn.invoke("AskTutor", "", q);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setStatus("error");
    }
  }

  return (
    <div className="card">
      <div className="flex items-center justify-between mb-3">
        <h2 className="font-heading font-semibold text-navy">Ask the AI Tutor</h2>
        {status === "done" && stats && (
          <span className="text-xs text-text-muted">
            {stats.totalTokens} tokens
          </span>
        )}
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
    </div>
  );
}
