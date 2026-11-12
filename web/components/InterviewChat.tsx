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

/**
 * Shown when an unfinished interview is resumed from history: the server
 * keeps the run itself, but past turns are not re-fetched, so the transcript
 * starts fresh here.
 */
const RESUMED_NOTE =
  "Interview resumed. Earlier turns aren't shown here, so the transcript starts fresh — answer to continue with the next question, or finish the interview for your review.";

/** Unwraps an ApiError/Error into user-facing text, with a generic fallback. */
function errorMessage(e: unknown): string {
  if (e instanceof ApiError) return e.message;
  if (e instanceof Error && e.message) return e.message;
  return "Something went wrong. Please try again.";
}

/** Raised when the deadline wins the race - the invoke itself keeps running. */
class InvokeTimeoutError extends Error {}

/**
 * Invokes a hub method with a deadline. A hub disconnect mid-interview
 * rejects the pending invoke, so the page always leaves the busy state - the
 * timeout only covers the case where the connection dies silently. `hint`
 * is appended to the timeout message so the caller can point at the
 * recovery path (the invoke may still finish on the server).
 */
async function invokeWithTimeout<T>(
  conn: HubConnection,
  method: string,
  args: (string | boolean)[],
  hint = ""
): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const deadline = new Promise<never>((_, reject) => {
    timer = setTimeout(
      () =>
        reject(
          new InvokeTimeoutError(
            "The interview server did not respond in time. Check your connection and try again." +
              hint
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

// ponytail: one mounted InterviewChat. The close callback must be
// registered exactly once per cached connection — HubConnection.onclose has
// no matching off(), so re-registering per mount would pile up handlers.
let closeHandler: ((err?: Error) => void) | null = null;
const closeRegistered = new WeakSet<HubConnection>();

/**
 * Registers the shared close callback on a connection exactly once. Called on
 * mount and again at the start of every action: stopAllConnections() (logout)
 * drops the cache, so the next getInterviewConnection() can return a fresh
 * object that has never seen the handler.
 */
function ensureCloseRegistered(conn: HubConnection): void {
  if (closeRegistered.has(conn)) return;
  closeRegistered.add(conn);
  conn.onclose((err) => closeHandler?.(err));
}

/**
 * Stops the connection after a timed-out invoke. Promise.race can only
 * abandon conn.invoke — the server keeps working and streaming on the same
 * connection, and those untagged tokens would land in the next turn.
 * Stopping drops that stream; the cached connection object survives, so the
 * next action just restarts it (CodeRabbit #176).
 */
async function stopAfterTimeout(conn: HubConnection): Promise<void> {
  try {
    await conn.stop();
  } catch {
    // already gone — nothing to clean up
  }
}

/**
 * The live mock interview (#129). Start and answer go over the interview hub
 * so the interviewer's tokens stream in live; completing the run is a plain
 * REST call. Mirrors TutorChat's session/stream structure rather than
 * inventing a third one. An unfinished interview from the history list
 * comes back through `resumeRequest` so it can be continued and finished.
 */
export default function InterviewChat({
  initialJdId,
  onCompleted,
  resumeRequest,
}: {
  initialJdId?: string;
  onCompleted?: () => void;
  /** A new object per history click; its id is the interview to resume. */
  resumeRequest?: { id: string } | null;
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
  // A resume request that arrived while the chat was busy; applied once the
  // current action finishes so it is never lost (CodeRabbit #176).
  const pendingResumeRef = useRef<string | null>(null);
  // The resume request we last consumed, so a prop that stays set does not
  // re-queue after the user has moved on to a different interview.
  const lastResumeRequestRef = useRef<{ id: string } | null>(null);
  // The last InterviewError message for the action in flight. The hub sends
  // the real reason and then returns null, so the generic "could not be
  // started" fallback must not replace it.
  const hubErrorRef = useRef<string | null>(null);
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
  // The disconnect handler is registered once per cached connection via
  // ensureCloseRegistered(); HubConnection.onclose has no off(), so
  // re-registering per mount would pile up handlers.
  useEffect(() => {
    closeHandler = (err) => {
      if (
        statusRef.current === "streaming" ||
        statusRef.current === "starting"
      ) {
        setError(err?.message ?? "Connection lost");
        updateStatus("error");
      }
    };
    ensureCloseRegistered(getInterviewConnection());
    // Handlers are registered per action below; nothing to clean here.
    // updateStatus is stable (useCallback [], []) and the module-level
    // helpers need no dependency.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // History → resume (#129 recovery, CodeRabbit #176): an unfinished row
  // hands its id back so the interview can be continued and finished instead
  // of dead-ending on "not completed". Skipped while a stream is in flight -
  // clobbering mid-turn would orphan the running invoke - and when the chat
  // is already on that interview, so a double-click cannot wipe the draft.
  //
  // The request is queued only when its identity changes (queue effect), and
  // applied only on status changes (apply effect). Tracking the last consumed
  // request separately prevents a prop that stays set from re-queuing after
  // the user has moved on to a different interview (CodeRabbit #176).
  useEffect(() => {
    if (
      resumeRequest &&
      (!lastResumeRequestRef.current ||
        resumeRequest.id !== lastResumeRequestRef.current.id)
    ) {
      pendingResumeRef.current = resumeRequest.id;
    }
  }, [resumeRequest]);

  useEffect(() => {
    const id = pendingResumeRef.current;
    if (!id) return;
    if (
      statusRef.current === "starting" ||
      statusRef.current === "streaming" ||
      statusRef.current === "completing"
    ) {
      return;
    }
    if (interviewIdRef.current === id) {
      pendingResumeRef.current = null;
      lastResumeRequestRef.current = resumeRequest ?? null;
      return;
    }
    pendingResumeRef.current = null;
    lastResumeRequestRef.current = resumeRequest ?? null;

    interviewIdRef.current = id;
    setInterviewId(id);
    setReview(null);
    setAnswer("");
    setOngoing(true);
    streamingTurnRef.current = null;
    setError(null);
    setTurns([
      {
        id: ++turnIdRef.current,
        role: "interviewer",
        text: RESUMED_NOTE,
      },
    ]);
    updateStatus("ready");
  }, [status, resumeRequest, updateStatus]);

  // Keep the newest line in view while the interviewer talks.
  useEffect(() => {
    const el = transcriptRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [turns]);

  /** Opens the empty interviewer bubble the next stream will fill. */
  const openInterviewerTurn = useCallback(() => {
    const id = ++turnIdRef.current;
    streamingTurnRef.current = id;
    setTurns((prev) => [...prev, { id, role: "interviewer", text: "" }]);
    return id;
  }, []);

  /**
   * Starts a fresh interview over the hub: registers the stream handlers,
   * invokes StartMock under the hang-guard, and falls back to the start
   * result's opening message when nothing streamed in. A timed-out start may
   * still have created the interview server-side, so the error points at the
   * history list where it can be picked back up.
   */
  const startInterview = useCallback(async () => {
    if (statusRef.current !== "idle" && statusRef.current !== "error" && statusRef.current !== "done") {
      return;
    }

    setError(null);
    hubErrorRef.current = null;
    setReview(null);
    setTurns([]);
    setAnswer("");
    setOngoing(true);
    interviewIdRef.current = null;
    setInterviewId(null);
    streamingTurnRef.current = null;
    updateStatus("starting");

    let conn: HubConnection | null = null;
    let turnId = -1;
    try {
      conn = getInterviewConnection();
      ensureCloseRegistered(conn);
      if (conn.state !== "Connected") await conn.start();

      turnId = openInterviewerTurn();

      conn.off("InterviewerMessage");
      conn.off("InterviewError");
      conn.on("InterviewerMessage", (token: string) => {
        const target = streamingTurnRef.current;
        if (target === null) return;
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
          const message = payload.message || "The interview failed.";
          hubErrorRef.current = message;
          setError(message);
          updateStatus("error");
        }
      );

      const result = await invokeWithTimeout<MockInterviewStartResult | null>(
        conn,
        "StartMock",
        [jdId.trim(), type, false],
        " If the interview did start anyway, it is listed under Interview history — open it there to continue."
      );
      streamingTurnRef.current = null;

      if (!result) throw new Error("The interview could not be started.");

      interviewIdRef.current = result.interviewId;
      setInterviewId(result.interviewId);
      // The opening normally streams in token by token; fall back to the
      // opening message the start result carries if nothing arrived.
      const openingId = ++turnIdRef.current;
      setTurns((prev) => openingTurns(prev, openingId, result.openingMessage));
      updateStatus("ready");
    } catch (e) {
      streamingTurnRef.current = null;
      // A stream that failed before the first token leaves an empty bubble.
      setTurns((prev) => dropEmptyTurn(prev, turnId));
      setError(hubErrorRef.current ?? errorMessage(e));
      updateStatus("error");
      // The deadline won: the invoke keeps running server-side, so drop the
      // connection to stop its tokens reaching a later turn.
      if (e instanceof InvokeTimeoutError && conn) void stopAfterTimeout(conn);
    }
  }, [jdId, type, openInterviewerTurn, updateStatus]);

  /**
   * Sends the candidate's answer over the hub and streams the interviewer's
   * reply into a new turn. The draft is only cleared once the server has
   * recorded the answer; on any failure the candidate turn is rolled back and
   * the draft stays in the box so the user can retry (CodeRabbit #176).
   */
  const answerInterview = useCallback(async () => {
    const message = answer.trim();
    const interviewId = interviewIdRef.current;
    if (!message || !interviewId) return;
    if (statusRef.current !== "ready" && statusRef.current !== "error") return;

    setError(null);
    hubErrorRef.current = null;
    // Optimistic candidate turn; rolled back unless the server records it.
    const candidateTurnId = ++turnIdRef.current;
    streamingTurnRef.current = candidateTurnId;
    setTurns((prev) => [
      ...prev,
      { id: candidateTurnId, role: "candidate", text: message },
    ]);
    updateStatus("starting");

    let conn: HubConnection | null = null;
    // True only when the hub returns a result, i.e. the server recorded the
    // candidate turn. InterviewError / null result means it did not.
    let serverRecorded = false;
    try {
      conn = getInterviewConnection();
      ensureCloseRegistered(conn);
      if (conn.state !== "Connected") await conn.start();

      openInterviewerTurn();

      conn.off("InterviewerMessage");
      conn.off("InterviewError");
      conn.on("InterviewerMessage", (token: string) => {
        const target = streamingTurnRef.current;
        if (target === null) return;
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
          // All questions spent (the resume case): close the answer box and
          // point at the finish button rather than letting the user retype
          // into an interview the server refuses to extend.
          const message = payload.message || "The interview failed.";
          if (/already ended/i.test(message)) setOngoing(false);
          hubErrorRef.current = message;
          setError(message);
          updateStatus("error");
        }
      );

      const result = await invokeWithTimeout<MockInterviewAnswerResult | null>(
        conn,
        "SendAnswer",
        [interviewId, message],
        " Your answer may already have been recorded — reopen the interview from Interview history to continue."
      );
      streamingTurnRef.current = null;

      if (!result) throw new Error("The interviewer did not respond.");
      serverRecorded = true;
      setOngoing(result.interviewOngoing);
      setAnswer("");
      updateStatus("ready");
    } catch (e) {
      streamingTurnRef.current = null;
      // Roll back unless the server recorded the answer: remove the
      // candidate turn and leave the draft in the box for a retry.
      if (!serverRecorded) {
        setTurns((prev) => prev.filter((t) => t.id !== candidateTurnId));
      }
      setError(hubErrorRef.current ?? errorMessage(e));
      updateStatus("error");
      // The deadline won: the invoke keeps running server-side, so drop the
      // connection to stop its tokens reaching a later turn.
      if (e instanceof InvokeTimeoutError && conn) void stopAfterTimeout(conn);
    }
  }, [answer, openInterviewerTurn, updateStatus]);

  /** Closes the run over REST and shows the review the server hands back. */
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
