"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { api, ApiError, type TextbookJob, type TextbookJobStatus } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import FetchError from "@/components/FetchError";
import { isGuid } from "@/lib/question-form";
import {
  CORPUS_MAX_BYTES,
  POLL_INTERVAL_MS,
  isTerminalStatus,
  progressPercent,
  splitUploadErrors,
  validateCorpusFile,
} from "@/lib/corpus-job";

const MAX_FILE_LABEL = `${CORPUS_MAX_BYTES / (1024 * 1024)} MB`;

export default function AdminCorpusPage() {
  // #102: guard, Navbar and sidebar come from app/admin/layout.tsx
  return <CorpusContent />;
}

function isContentAdminRoles(roles: string[]): boolean {
  return roles.includes("SuperAdmin") || roles.includes("ContentAdmin");
}

function formatTimestamp(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleString();
}

function CorpusContent() {
  const { user, setUser } = useAuth();
  const router = useRouter();
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  const [file, setFile] = useState<File | null>(null);
  const [conceptId, setConceptId] = useState("");
  const [fieldErrors, setFieldErrors] = useState<{ file?: string; concept?: string }>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);

  const [job, setJob] = useState<TextbookJob | null>(null);
  const [pollError, setPollError] = useState<string | null>(null);
  const [pollAttempt, setPollAttempt] = useState(0);

  const isContentAdmin =
    !!user?.roles?.includes("SuperAdmin") || !!user?.roles?.includes("ContentAdmin");

  // Deliberate guard layering (#82): the shell admits any admin role, but this page
  // maps the ManageContent policy (ContentAdmin, SuperAdmin) — the server 403s
  // everyone else, this redirect just keeps the UI honest.
  useEffect(() => {
    if (user && !isContentAdmin) router.replace("/dashboard");
  }, [user, isContentAdmin, router]);

  // Stale identity (expired session or server-side demotion): re-verify instead of a
  // dead-end failure — me() 401s -> setUser(null) -> AuthProvider routes to /login. A
  // demoted caller gets refreshed roles and the guard reroutes. If the refresh does not
  // explain the 401/403, onStillAuthorized surfaces it so the caller never stalls mute.
  const refreshIdentity = useCallback(
    (onStillAuthorized?: () => void) => {
      api
        .me()
        .then((me) => {
          setUser(me);
          if (!isContentAdminRoles(me.roles)) {
            router.replace("/dashboard");
            return;
          }
          onStillAuthorized?.();
        })
        .catch(() => setUser(null));
    },
    [setUser, router]
  );

  // One request per tick — no overlapping polls, no interval to clear. The job object
  // changing on every response is what schedules the next tick; a terminal status
  // stops it. A failed poll keeps the last known progress and waits for Retry.
  useEffect(() => {
    if (!job || isTerminalStatus(job.status)) return;
    let cancelled = false;
    const timer = setTimeout(() => {
      api
        .adminTextbookJob(job.jobId)
        .then((next) => {
          if (cancelled) return;
          setJob(next);
          setPollError(null);
        })
        .catch((err: unknown) => {
          if (cancelled) return;
          if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
            refreshIdentity(() => setPollError(err.message));
            return;
          }
          setPollError(err instanceof ApiError ? err.message : "Lost contact with the server.");
        });
    }, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [job, pollAttempt, refreshIdentity]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (uploading) return;

    // client-side mirror of UploadTextbookValidator — avoids a round trip and a 20 MB
    // body for errors the server would reject anyway; the server still has final say.
    const trimmedConceptId = conceptId.trim();
    const errors: { file?: string; concept?: string } = {};
    const fileError = file ? validateCorpusFile(file) : "Choose a .pdf or .txt file to upload.";
    if (fileError) errors.file = fileError;
    if (trimmedConceptId && !isGuid(trimmedConceptId)) {
      errors.concept = "Concept ID must be a valid GUID.";
    }
    if (!file || Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      setBanner(null); // never let a previous attempt's banner outlive the fix
      return;
    }

    setFieldErrors({});
    setBanner(null);
    setUploading(true);
    try {
      const accepted = await api.adminUploadTextbook(file, trimmedConceptId || undefined);
      setPollError(null);
      // The API answers 202 before a worker touches the row: Pending is the honest
      // first state, and the poll effect picks up from there.
      setJob({
        jobId: accepted.jobId,
        fileName: file.name,
        status: "Pending",
        totalChunks: 0,
        processedChunks: 0,
        error: null,
        createdAt: new Date().toISOString(),
        completedAt: null,
      });
      // Clear the picker so an accepted file cannot be uploaded twice by reflex. A
      // *failed* upload keeps it, so retrying is a single click.
      setFile(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        refreshIdentity(() => setBanner(err.message));
      } else if (err instanceof ApiError && err.status === 404 && /concept/i.test(err.message)) {
        setFieldErrors({ concept: "Concept not found. Check the Concept ID and try again." });
      } else if (err instanceof ApiError && err.errors && Object.keys(err.errors).length > 0) {
        // keys with a rendered field go next to it; the rest land in the banner so a
        // failed upload never leaves the admin without an explanation
        const split = splitUploadErrors(err.errors);
        const mapped: { file?: string; concept?: string } = {};
        if (split.fileError) mapped.file = split.fileError;
        if (split.conceptError) mapped.concept = split.conceptError;
        setFieldErrors(mapped);
        if (split.banner) setBanner(split.banner);
      } else if (err instanceof ApiError && err.status === 413) {
        // Kestrel's request-size limit answers without the JSON envelope, so
        // there is no server message to split into fields.
        setFieldErrors({ file: "File is too large for the server upload limit." });
      } else {
        setBanner(
          err instanceof ApiError ? err.message : "Something went wrong. Please try again."
        );
      }
    } finally {
      setUploading(false);
    }
  }

  if (!user || !isContentAdmin) return null;

  return (
    <>
      <h1 className="text-2xl font-heading font-bold text-text-heading">Corpus upload</h1>
      <p className="text-sm text-text-muted mt-1">
        Upload textbook or article content (.pdf/.txt, up to {MAX_FILE_LABEL}) to chunk, embed and
        map it into the tutor&apos;s knowledge base.
      </p>

      <section className="card-flat mt-6 mb-8" aria-labelledby="corpus-upload-heading">
        <h2
          id="corpus-upload-heading"
          className="text-xl font-heading font-bold text-text-heading mb-6"
        >
          Upload content
        </h2>

        <form onSubmit={handleSubmit} noValidate>
          {banner && (
            <div className="error-banner" role="alert">
              <svg
                width="16"
                height="16"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
                aria-hidden="true"
              >
                <circle cx="12" cy="12" r="10" />
                <line x1="15" x2="9" y1="9" y2="15" />
                <line x1="9" x2="15" y1="9" y2="15" />
              </svg>
              {banner}
            </div>
          )}

          <div className="input-group">
            <label htmlFor="corpus-file">
              File{" "}
              <span className="font-normal text-text-muted">
                (.pdf or .txt, max {MAX_FILE_LABEL})
              </span>
            </label>
            <input
              id="corpus-file"
              ref={fileInputRef}
              type="file"
              accept=".pdf,.txt"
              disabled={uploading}
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              aria-invalid={!!fieldErrors.file}
              aria-describedby={fieldErrors.file ? "corpus-file-error" : undefined}
            />
            <FieldError id="corpus-file-error" message={fieldErrors.file} />
          </div>

          <div className="input-group">
            <label htmlFor="corpus-concept">
              Concept ID <span className="font-normal text-text-muted">(optional)</span>
            </label>
            <input
              id="corpus-concept"
              type="text"
              value={conceptId}
              disabled={uploading}
              onChange={(e) => setConceptId(e.target.value)}
              placeholder="e.g. 3f2504e0-4f89-11d3-9a0c-0305e82c3301"
              aria-invalid={!!fieldErrors.concept}
              aria-describedby={
                fieldErrors.concept ? "corpus-concept-hint corpus-concept-error" : "corpus-concept-hint"
              }
            />
            <p id="corpus-concept-hint" className="mt-1.5 text-xs text-text-muted">
              Every chunk of this file is mapped to that concept. Leave blank to ingest it without
              a concept mapping.
            </p>
            <FieldError id="corpus-concept-error" message={fieldErrors.concept} />
          </div>

          <button
            type="submit"
            disabled={uploading}
            aria-busy={uploading}
            className="px-5 py-2.5 text-sm font-semibold text-white bg-primary hover:bg-primary-dark rounded-xl transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {uploading ? (
              <span className="flex items-center justify-center gap-2">
                <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                </svg>
                Uploading...
              </span>
            ) : (
              "Upload & ingest"
            )}
          </button>
        </form>
      </section>

      {job && (
        <JobPanel
          job={job}
          pollError={pollError}
          onRetryPoll={() => {
            setPollError(null);
            setPollAttempt((n) => n + 1);
          }}
        />
      )}
    </>
  );
}

function FieldError({ id, message }: { id: string; message: string | undefined }) {
  if (!message) return null;
  return (
    <p id={id} role="alert" className="mt-1.5 text-xs font-medium text-[#B91C1C]">
      {message}
    </p>
  );
}

const STATUS_BADGE: Record<TextbookJobStatus, string> = {
  Pending: "bg-[#FEF3C7] text-[#B45309]",
  Processing: "bg-accent-light-blue text-primary",
  Completed: "bg-[#DCFCE7] text-[#15803D]",
  Failed: "bg-[#FEE2E2] text-[#B91C1C]",
};

function JobPanel({
  job,
  pollError,
  onRetryPoll,
}: {
  job: TextbookJob;
  pollError: string | null;
  onRetryPoll: () => void;
}) {
  const percent = progressPercent(job.totalChunks, job.processedChunks);
  const terminal = isTerminalStatus(job.status);

  return (
    <section className="card-flat" aria-labelledby="corpus-job-heading">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 id="corpus-job-heading" className="text-xl font-heading font-bold text-text-heading">
            Ingestion job
          </h2>
          <p className="text-sm text-text-muted mt-1 break-all" title={job.fileName}>
            {job.fileName}
          </p>
        </div>
        <span
          className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold ${STATUS_BADGE[job.status]}`}
        >
          {job.status}
        </span>
      </div>

      {job.status === "Failed" ? (
        <div className="error-banner mt-5" role="alert">
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <circle cx="12" cy="12" r="10" />
            <line x1="15" x2="9" y1="9" y2="15" />
            <line x1="9" x2="15" y1="9" y2="15" />
          </svg>
          <span>
            {job.error ?? "Ingestion failed."} Upload the file again to retry — failed jobs are not
            re-run.
          </span>
        </div>
      ) : (
        <div className="mt-5">
          <div
            className="h-2 rounded-full bg-border overflow-hidden"
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={percent}
            aria-valuetext={`${percent}%`}
            aria-label={`Embedding ${job.fileName}`}
          >
            <div
              className="h-full bg-primary transition-[width] duration-500 ease-out"
              style={{ width: `${percent}%` }}
            />
          </div>
          {job.status === "Completed" ? (
            <p className="text-sm font-medium text-[#15803D] mt-3" role="status">
              {job.totalChunks} chunk{job.totalChunks === 1 ? "" : "s"} indexed
              {job.completedAt ? ` at ${formatTimestamp(job.completedAt)}` : ""}.
            </p>
          ) : (
            <p className="text-sm text-text-muted mt-2" role="status">
              {job.totalChunks > 0
                ? `${job.processedChunks} / ${job.totalChunks} chunks embedded (${percent}%)`
                : job.status === "Pending"
                  ? "Queued — waiting for an ingestion worker."
                  : "Reading the file and counting chunks..."}
            </p>
          )}
        </div>
      )}

      <p className="text-xs text-text-muted mt-4">
        Job <span className="font-mono">{job.jobId}</span> · started{" "}
        {formatTimestamp(job.createdAt)}
        {!terminal && !pollError && ` · refreshing every ${POLL_INTERVAL_MS / 1000}s`}
      </p>

      {pollError && (
        <div className="mt-4">
          <FetchError message={pollError} onRetry={onRetryPoll} />
        </div>
      )}
    </section>
  );
}
