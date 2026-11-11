"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { getInterviewConnection } from "@/lib/signalr";
import {
  api,
  ApiError,
  type MockInterviewAnswerResult,
  type MockInterviewReview,
  type MockInterviewStartResult,
} from "@/lib/api";
import InterviewReviewCard from "@/components/InterviewReviewCard";
import {
  appendToken,
  dropEmptyTurn,
  openingTurns,
  type InterviewTurn,
} from "@/lib/interview";

type Status =
  | "idle"
  | "starting"
  | "streaming"
  | "ready"
  | "completing"
  | "done"
  | "error";

/**
 * Generation runs on the server and can take a while; this is only a
 * hang-guard so a dropped hub surfaces an error instead of a spinner that
 * never ends.
 */
const INVOKE_TIMEOUT_MS = 120_000;

function errorMessage(e: unknown): string {
  if (e instanceof ApiError) return e.message;
  if (e instanceof Error && e.message) return e.message;
  return "Something went wrong. Please try again.";
}

/**
 * Invokes a hub method with a deadline. A hub disconnect mid-interview
 * rejects the pending invoke, so the page always leaves the busy state — the
 * timeout only covers the case where the connection dies silently.
 */
async function invokeWithTimeout<T>(
  conn: HubConnection,
  method: string,
  args: (string | boolean)[]
): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const deadline = new Promise<never>((_, reject) => {
    timer = setTimeout(
      () =>
        reject(
          new Error(
            "The interview server did not respond in time. Check your connection and try again."
          )
        ),
      INVOKE_TIMEOUT_MS
    );
  });
  try {
    return await Promise.race([conn.invoke<T>(method, ...args), deadline]);
  } finally {
    if (timer) clearTimeout(timer);
  }
}

/**
 * The live mock interview (#129). Start and answer go over the interview hub
 * so the interviewer's tokens stream in live; completing the run is a plain
 * REST call. Mirrors TutorChat's session/stream structure rather than
 * inventing a third one.
 */
// ponytail: one mounted InterviewChat. The close callback must be
// registered exactly once per cached connection — HubConnection.onclose has
// no matching off(), so re-registering per mount would pile up handlers.
let closeHandler: ((err?: Error) => void) | null = null;
const closeRegistered = new WeakSet<HubConnection>();

export default function InterviewChat({
  initialJdId,
  onCompleted,
}: {
  initialJdId?: string;
  onCompleted?: () => void;
}) {
  const [jdId, setJdId] = useState(initialJdId ?? "");
  const [type, setType] = useState("Technical");
  const [status, setStatus] = useState<Status>("idle");
  const [error, setError] = useState<string | null>(null);
  const [turns, setTurns] = useState<InterviewTurn[]>([]);
  const [answer, setAnswer] = useState("");
  const [review, setReview] = useState<MockInterviewReview | null>(null);
  const [ongoing, setOngoing] = useState(true);
  const [interviewId, setInterviewId] = useState<string | null>(null);

  const interviewIdRef = useRef<string | null>(null);
  // The turn the next streamed token belongs to; null when nothing streams.
  const streamingTurnRef = useRef<number | null>(null);
  const turnIdRef = useRef(0);
  const statusRef = useRef<Status>("idle");
  const transcriptRef = useRef<HTMLDivElement | null>(null);

  const updateStatus = useCallback((next: Status) => {
    statusRef.current = next;
    setStatus(next);
  }, []);

  const busy =
    status === "starting" || status === "streaming" || status === "completing";
  const hasInterview = interviewId !== null;
  // The setup is locked while a run is in flight; once it is over the job
  // description and the format can be changed for the next run.
  const locked = busy || (hasInterview && status !== "done");
  // The interviewer has no problems left - only /complete may close the run.
  const canAnswer = !busy && ongoing;

// Streamed tokens land in the turn opened just before the invoke. Handlers
// are registered per action (mirroring TutorChat's ask()), scoped to the
// current turn/interview via refs so they never close over stale state.
// The disconnect handler is registered once per cached connection:
// HubConnection.onclose has no off(), so re-registering per mount would pile
// up handlers.
useEffect(() => {
  const conn = getInterviewConnection();
  closeHandler = (err) => {
    if (
      statusRef.current === "streaming" ||
      statusRef.current === "starting"
    ) {
      setError(err?.message ?? "Connection lost");
      updateStatus("error");
    }
  };
  if (!closeRegistered.has(conn)) {
    closeRegistered.add(conn);
    conn.onclose((err) => closeHandler?.(err));
  }
  // Handlers are registered per action below; nothing to clean here.
  // updateStatus is stable (useCallback [], []) and closeRegistered is
  // module-level, so neither needs to be a dependency.
  // eslint-disable-next-line react-hooks/exhaustive-deps
}, []);

  // Keep the newest line in view while the interviewer talks.
  useEffect(() => {
    const el = transcriptRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [turns]);

  const openInterviewerTurn = useCallback(() => {
    const id = ++turnIdRef.current;
    streamingTurnRef.current = id;
    setTurns((prev) => [...prev, { id, role: "interviewer", text: "" }]);
    return id;
  }, []);

  const startInterview = useCallback(async () => {
    if (statusRef.current !== "idle" && statusRef.current !== "error" && statusRef.current !== "done") {
      return;
    }

    setError(null);
    setReview(null);
    setTurns([]);
    setAnswer("");
    setOngoing(true);
    interviewIdRef.current = null;
    setInterviewId(null);
    streamingTurnRef.current = null;
    updateStatus("starting");

    let turnId = -1;
    try {
      const conn = getInterviewConnection();
      if (conn.state !== "Connected") await conn.start();

      turnId = openInterviewerTurn();

      conn.off("InterviewerMessage");
      conn.off("InterviewError");
      conn.on("InterviewerMessage", (token: string) => {
        const target = streamingTurnRef.current;
        if (target == null) return;
        setTurns((prev) => appendToken(prev, target, token));
        if (statusRef.current !== "streaming") updateStatus("streaming");
      });
      conn.on(
        "InterviewError",
        (payload: { interviewId: string | null; message: string }) => {
          if (
            payload.interviewId &&
            payload.interviewId !== interviewIdRef.current
          ) {
            return;
          }
          setError(payload.message || "The interview failed.");
          updateStatus("error");
        }
      );

      const result = await invokeWithTimeout<MockInterviewStartResult | null>(
        conn,
        "StartMock",
        [jdId.trim(), type, false]
      );
      streamingTurnRef.current = null;

      if (!result) throw new Error("The interview could not be started.");

      interviewIdRef.current = result.interviewId;
      setInterviewId(result.interviewId);
      // The opening normally streams in token by token; fall back to the
      // opening message the start result carries if nothing arrived.
      setTurns((prev) =>
        openingTurns(prev, ++turnIdRef.current, result.openingMessage)
      );
      updateStatus("ready");
    } catch (e) {
      streamingTurnRef.current = null;
      // A stream that failed before the first token leaves an empty bubble.
      setTurns((prev) => dropEmptyTurn(prev, turnId));
      setError(errorMessage(e));
      updateStatus("error");
    }
  }, [jdId, type, openInterviewerTurn, updateStatus]);

  const answerInterview = useCallback(async () => {
    const message = answer.trim();
    const interviewId = interviewIdRef.current;
    if (!message || !interviewId) return;
    if (statusRef.current !== "ready" && statusRef.current !== "error") return;

    setError(null);
    setAnswer("");
    setTurns((prev) => [
      ...prev,
      { id: ++turnIdRef.current, role: "candidate", text: message },
    ]);
    updateStatus("starting");

    let turnId = -1;
    try {
      const conn = getInterviewConnection();
      if (conn.state !== "Connected") await conn.start();

      turnId = openInterviewerTurn();

      conn.off("InterviewerMessage");
      conn.off("InterviewError");
      conn.on("InterviewerMessage", (token: string) => {
        const target = streamingTurnRef.current;
        if (target == null) return;
        setTurns((prev) => appendToken(prev, target, token));
        if (statusRef.current !== "streaming") updateStatus("streaming");
      });
      conn.on(
        "InterviewError",
        (payload: { interviewId: string | null; message: string }) => {
          if (
            payload.interviewId &&
            payload.interviewId !== interviewIdRef.current
          ) {
            return;
          }
          setError(payload.message || "The interview failed.");
          updateStatus("error");
        }
      );

      const result = await invokeWithTimeout<MockInterviewAnswerResult | null>(
        conn,
        "SendAnswer",
        [interviewId, message]
      );
      streamingTurnRef.current = null;

      if (!result) throw new Error("The interviewer did not respond.");
      setOngoing(result.interviewOngoing);
      updateStatus("ready");
    } catch (e) {
      streamingTurnRef.current = null;
      // A stream that failed before the first token leaves an empty bubble.
      setTurns((prev) => dropEmptyTurn(prev, turnId));
      setError(errorMessage(e));
      updateStatus("error");
    }
  }, [answer, openInterviewerTurn, updateStatus]);

  const completeInterview = useCallback(async () => {
    const interviewId = interviewIdRef.current;
    if (!interviewId) return;
    if (statusRef.current === "starting" || statusRef.current === "streaming") {
      return;
    }

    setError(null);
    updateStatus("completing");
    try {
      const result = await api.mockComplete(interviewId);
      setReview(result);
      setOngoing(false);
      updateStatus("done");
      onCompleted?.();
    } catch (e) {
      setError(errorMessage(e));
      updateStatus("ready");
    }
  }, [onCompleted, updateStatus]);

  return (
    <div className="card !p-5 flex flex-col gap-5 min-w-0">
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label
            htmlFor="interview-jd"
            className="block text-xs font-medium text-text-muted mb-1"
          >
            Job description id (optional)
          </label>
          <input
            id="interview-jd"
            type="text"
            value={jdId}
            onChange={(e) => setJdId(e.target.value)}
            disabled={locked}
            placeholder="e.g. 0f3c…"
            className="w-full text-sm rounded-[14px] border-[1.5px] border-border bg-white px-4 py-3 text-text-body focus:border-primary focus:shadow-[0_0_0_3px_rgba(74,173,219,0.12)] focus:outline-none disabled:opacity-60"
          />
          <p className="text-xs text-text-muted mt-1">
            <Link
              href="/jd"
              className="text-primary hover:underline font-medium"
            >
              Upload a job description
            </Link>{" "}
            to get an id.
          </p>
        </div>
        <div>
          <label
            htmlFor="interview-type"
            className="block text-xs font-medium text-text-muted mb-1"
          >
            Format
          </label>
          <select
            id="interview-type"
            value={type}
            onChange={(e) => setType(e.target.value)}
            disabled={locked}
            className="w-full text-sm rounded-[14px] border-[1.5px] border-border bg-white px-4 py-3 text-text-body focus:border-primary focus:shadow-[0_0_0_3px_rgba(74,173,219,0.12)] focus:outline-none cursor-pointer disabled:opacity-60"
          >
            <option value="Technical">Technical</option>
            <option value="Behavioral">Behavioral</option>
          </select>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <button
          onClick={startInterview}
          disabled={busy || (hasInterview && status !== "done")}
          className="btn-primary !py-2.5 !px-6 !text-sm"
        >
          {status === "done" ? "Start another interview" : "Start interview"}
        </button>
        {status === "streaming" && (
          <span className="text-xs text-primary animate-pulse">
            Interviewer is speaking…
          </span>
        )}
        {status === "ready" && !ongoing && (
          <span className="text-xs text-text-muted">
            That was the last problem — finish the interview for your review.
          </span>
        )}
        {status === "done" && (
          <span className="text-xs text-success">Interview complete.</span>
        )}
      </div>

      {error && (
        <div className="error-banner !mb-0" role="alert">
          <span aria-hidden="true">⚠️</span>
          <span>{error}</span>
        </div>
      )}

      {turns.length > 0 && (
        <div
          ref={transcriptRef}
          className="flex flex-col gap-3 max-h-[26rem] overflow-y-auto pr-1 min-w-0"
        >
          {turns.map((turn) => (
            <div
              key={turn.id}
              className={turn.role === "candidate" ? "flex justify-end" : ""}
            >
              <div
                className={`max-w-[92%] rounded-[14px] px-4 py-2.5 text-sm leading-relaxed whitespace-pre-wrap break-words ${
                  turn.role === "candidate"
                    ? "rounded-br-sm bg-accent-light-blue text-text-heading"
                    : "rounded-bl-sm bg-bg-light text-text-heading"
                }`}
              >
                {turn.text}
              </div>
            </div>
          ))}
        </div>
      )}

      {hasInterview && status !== "done" && (
        <div className="flex flex-col gap-3">
          <div>
            <label
              htmlFor="interview-answer"
              className="block text-xs font-medium text-text-muted mb-1"
            >
              Your answer
            </label>
            <textarea
              id="interview-answer"
              value={answer}
              onChange={(e) => setAnswer(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  answerInterview();
                }
              }}
              rows={3}
              disabled={!canAnswer}
              placeholder="Type your answer… (Enter to send, Shift+Enter for newline)"
              className="w-full p-3 text-sm text-text-heading bg-white border-[1.5px] border-[var(--border)] rounded-[14px] outline-none focus:border-primary transition-all resize-none disabled:opacity-60"
            />
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <button
              onClick={answerInterview}
              disabled={!canAnswer || answer.trim().length === 0}
              className="btn-primary !py-2.5 !px-6 !text-sm"
            >
              {busy ? "Waiting…" : "Send answer"}
            </button>
            <button
              onClick={completeInterview}
              disabled={busy}
              className="px-4 py-2 text-sm font-medium text-primary border border-border rounded-xl hover:bg-accent-light-blue transition-colors cursor-pointer disabled:opacity-60"
            >
              Finish and get review
            </button>
          </div>
        </div>
      )}

      {review && (
        <div className="border-t border-border pt-5">
          <h2 className="font-heading font-semibold text-navy text-lg mb-4">
            Interview review
          </h2>
          <InterviewReviewCard review={review} />
        </div>
      )}
    </div>
  );
}
