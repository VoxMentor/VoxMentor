"use client";

import { Suspense, useEffect, useRef, useState, type ReactNode } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, ApiError, type JdDetail } from "@/lib/api";
import { severityColor } from "@/lib/severity";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";
import SkeletonLoader from "@/components/SkeletonLoader";

const MIN_CHARS = 50;
const MAX_CHARS = 20000;

const secondaryButtonClass =
  "px-4 py-2 text-sm font-medium text-primary border border-border rounded-xl hover:bg-accent-light-blue transition-colors cursor-pointer";

function clampPct(value: number): number {
  return Math.max(0, Math.min(100, value * 100));
}

function PageSkeleton() {
  return (
    <div className="space-y-6">
      <div className="card">
        <div className="h-6 w-48 bg-border rounded animate-pulse mb-4" />
        <div className="h-12 w-32 bg-border rounded animate-pulse mb-3" />
        <div className="h-3 w-full bg-border rounded-full animate-pulse" />
      </div>
      <div className="grid gap-6 md:grid-cols-2">
        {Array.from({ length: 2 }).map((_, i) => (
          <div key={i} className="card">
            <div className="h-5 w-36 bg-border rounded animate-pulse mb-5" />
            <div className="space-y-4">
              {Array.from({ length: 4 }).map((_, j) => (
                <div key={j} className="h-8 bg-border rounded animate-pulse" />
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function WeightCard({
  title,
  subtitle,
  skills,
  barClass,
}: {
  title: string;
  subtitle: string;
  skills: Record<string, number>;
  barClass: string;
}) {
  const entries = Object.entries(skills).sort((a, b) => b[1] - a[1]);

  return (
    <div className="card">
      <h2 className="font-heading font-semibold text-navy text-lg">{title}</h2>
      <p className="text-sm text-text-muted mt-1 mb-5">{subtitle}</p>

      {entries.length === 0 ? (
        <p className="text-sm text-text-muted">No skills recognised in this category.</p>
      ) : (
        <div className="space-y-4">
          {entries.map(([topic, weight]) => (
            <div key={topic}>
              <div className="flex items-baseline justify-between gap-3 mb-1.5">
                <span className="text-sm font-medium text-text-heading break-words">{topic}</span>
                <span className="text-xs text-text-muted shrink-0">
                  {Math.round(weight * 100)}%
                </span>
              </div>
              <div className="h-2 rounded-full bg-border overflow-hidden">
                <div
                  className={`h-full rounded-full ${barClass}`}
                  style={{ width: `${clampPct(weight)}%` }}
                />
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function JdDetailView({ jd, onReset }: { jd: JdDetail; onReset: () => void }) {
  const gaps = [...jd.gaps].sort((a, b) => b.severity - a.severity);
  const score = Math.round(jd.readinessScore);

  return (
    <div className="space-y-6">
      <div className="card">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-wider text-text-muted">
              {jd.difficulty}
            </p>
            <h2 className="font-heading text-2xl font-bold text-navy mt-1 break-words">
              {jd.role}
            </h2>
            <p className="text-text-body mt-0.5 break-words">{jd.companyName}</p>
          </div>
          <button
            onClick={onReset}
            className={secondaryButtonClass}
          >
            Upload a different JD
          </button>
        </div>
      </div>

      <div className="card">
        <h2 className="font-heading font-semibold text-navy text-lg">Interview readiness</h2>
        <div className="flex flex-col sm:flex-row sm:items-end gap-6 mt-4">
          <p className="font-heading text-5xl font-bold text-primary leading-none shrink-0">
            {score}
            <span className="text-2xl text-text-muted">/100</span>
          </p>
          <div className="flex-1 w-full min-w-0">
            <div className="h-3 rounded-full bg-border overflow-hidden">
              <div
                className="h-full rounded-full bg-gradient-to-r from-primary to-primary-light"
                style={{ width: `${Math.max(0, Math.min(100, score))}%` }}
              />
            </div>
            <div className="flex flex-wrap justify-between gap-x-4 gap-y-1 text-xs text-text-muted mt-2">
              <span>
                {gaps.length} {gaps.length === 1 ? "skill gap" : "skill gaps"}
              </span>
              <span>~{jd.estimatedWeeks} {jd.estimatedWeeks === 1 ? "week" : "weeks"} to ready</span>
            </div>
          </div>
        </div>
      </div>

      <div className="grid gap-6 md:grid-cols-2">
        <WeightCard
          title="Technical skills"
          subtitle="What the role weights on the technical side."
          skills={jd.technicalSkills}
          barClass="bg-gradient-to-r from-primary to-primary-light"
        />
        <WeightCard
          title="HR skills"
          subtitle="What the role weights on the behavioural side."
          skills={jd.hrSkills}
          barClass="bg-gradient-to-r from-accent-gold to-yellow-300"
        />
      </div>

      <div className="card">
        <h2 className="font-heading font-semibold text-navy text-lg mb-4">Gaps to close</h2>
        {gaps.length === 0 ? (
          <p className="text-sm text-text-muted">
            No gaps for this role — keep practising to stay sharp.
          </p>
        ) : (
          <ul className="space-y-3">
            {gaps.map((gap) => (
              <li
                key={gap.topic}
                className="flex gap-3 rounded-xl border border-border p-4"
              >
                <span
                  className={`mt-1.5 w-2.5 h-2.5 rounded-full shrink-0 ${severityColor(gap.severity)}`}
                />
                <div className="min-w-0">
                  <div className="flex flex-wrap items-baseline justify-between gap-x-3">
                    <p className="font-medium text-navy break-words">{gap.topic}</p>
                    <p className="text-xs text-text-muted shrink-0">
                      {Math.round(gap.severity * 100)}% gap
                    </p>
                  </div>
                  <p className="text-sm text-text-body mt-0.5">{gap.recommendation}</p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="card">
        <h2 className="font-heading font-semibold text-navy text-lg mb-4">
          Week-by-week roadmap
        </h2>
        {jd.roadmap.length === 0 ? (
          <p className="text-sm text-text-muted">No roadmap needed — nothing to close.</p>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {jd.roadmap.map((week) => (
              <div
                key={week.week}
                className="rounded-xl border border-border bg-bg-light p-4"
              >
                <p className="text-xs font-semibold uppercase tracking-wider text-primary">
                  Week {week.week}
                </p>
                <p className="font-medium text-navy mt-1 break-words">{week.focus}</p>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function UploadForm({ onUploaded }: { onUploaded: (jdId: string) => void }) {
  const [text, setText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Ignore the upload result if the form unmounted (e.g. user navigated away mid-parse).
  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const trimmedLength = text.trim().length;
  const tooShort = trimmedLength < MIN_CHARS;
  const tooLong = text.length > MAX_CHARS;
  const lengthInvalid = tooLong || (tooShort && text.length > 0);

  let lengthHint = "Looks good";
  if (tooLong) lengthHint = `Too long — trim to ${MAX_CHARS} characters`;
  else if (tooShort) lengthHint = `At least ${MIN_CHARS} characters (${trimmedLength} so far)`;

  async function handleSubmit() {
    setError(null);
    setSubmitting(true);
    try {
      const result = await api.jdUpload(text);
      if (!alive.current) return;
      onUploaded(result.jdId);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Could not analyse that job description. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  async function handleFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;

    if (!/\.(txt|md)$/i.test(file.name)) {
      setError(
        file.name.toLowerCase().endsWith(".pdf")
          ? "PDFs can't be read in the browser here — open the PDF and paste its text instead."
          : "Only .txt and .md files are supported — paste the text instead."
      );
      return;
    }

    try {
      setText(await file.text());
      setError(null);
    } catch {
      setError("Could not read that file — paste the text instead.");
    }
  }

  return (
    <div className="card max-w-3xl">
      <h2 className="font-heading font-semibold text-navy text-lg">Add a job description</h2>
      <p className="text-sm text-text-muted mt-1 mb-5">
        Paste the full JD ({MIN_CHARS}–{MAX_CHARS} characters) or load a
        plain-text file.
      </p>

      {error && (
        <div className="error-banner" role="alert">
          <span aria-hidden="true">⚠️</span>
          <span>{error}</span>
        </div>
      )}

      <div className="input-group">
        <label htmlFor="jd-text">Job description</label>
        <textarea
          id="jd-text"
          rows={12}
          value={text}
          onChange={(e) => {
            setText(e.target.value);
            if (error) setError(null);
          }}
          disabled={submitting}
          placeholder="Paste the job description here — responsibilities, required skills, tech stack…"
          className="resize-y min-h-48"
        />
        <div className="flex flex-wrap justify-between gap-x-4 gap-y-1 text-xs text-text-muted mt-2">
          <span className={lengthInvalid ? "text-danger" : undefined}>
            {lengthHint}
          </span>
          <span className={tooLong ? "text-danger" : undefined}>
            {text.length} / {MAX_CHARS}
          </span>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-4">
        <button
          onClick={handleSubmit}
          disabled={submitting || tooShort || tooLong}
          className="btn-primary"
        >
          {submitting && (
            <span className="w-4 h-4 border-2 border-white/40 border-t-white rounded-full animate-spin" />
          )}
          {submitting ? "Analysing…" : "Analyse JD"}
        </button>

        <label className="text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl px-3 py-2 cursor-pointer transition-colors focus-within:ring-2 focus-within:ring-primary/40">
          Load a .txt / .md file
          <input
            type="file"
            accept=".txt,.md,text/plain,text/markdown"
            onChange={handleFile}
            disabled={submitting}
            className="sr-only"
          />
        </label>
      </div>
    </div>
  );
}

function JdRoute() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const jdId = searchParams.get("jd") ?? undefined;

  // Keyed by the requested id (not the server echo) so stale/normalised ids can't wedge loading.
  const [detail, setDetail] = useState<{ jdId: string; data: JdDetail } | null>(null);
  // Failure is keyed by id so a stale message never shows for a different JD.
  const [failure, setFailure] = useState<{ jdId: string; message: string } | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!jdId) return;
    let cancelled = false;
    api
      .jd(jdId)
      .then((data) => {
        if (cancelled) return;
        setDetail({ jdId, data });
        setFailure(null);
      })
      .catch((e) => {
        if (cancelled) return;
        setFailure({
          jdId,
          message:
            e instanceof ApiError ? e.message : "Could not load this job description.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [jdId, attempt]);

  const error = failure && failure.jdId === jdId ? failure.message : null;
  const loading = Boolean(jdId) && !error && detail?.jdId !== jdId;
  const retry = () => {
    setFailure(null);
    setAttempt((n) => n + 1);
  };

  let body: ReactNode;
  if (!jdId) {
    body = (
      <UploadForm
        onUploaded={(id) => router.replace(`/jd?jd=${encodeURIComponent(id)}`)}
      />
    );
  } else if (error) {
    body = (
      <div className="space-y-3">
        <FetchError message={error} onRetry={retry} />
        <button onClick={() => router.replace("/jd")} className={secondaryButtonClass}>
          Upload a different JD
        </button>
      </div>
    );
  } else if (loading || !detail) {
    body = <PageSkeleton />;
  } else {
    body = <JdDetailView jd={detail.data} onReset={() => router.replace("/jd")} />;
  }

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="max-w-6xl mx-auto px-6 py-10">
          <div className="mb-8">
            <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
              Job Description
            </h1>
            <p className="text-text-muted mt-2 max-w-2xl">
              Paste a job description to see how your skills weigh against it, what to
              close first, and a week-by-week plan.
            </p>
          </div>

          {body}
        </main>
      </div>
    </ProtectedRoute>
  );
}

export default function JdPage() {
  return (
    <Suspense fallback={<SkeletonLoader />}>
      <JdRoute />
    </Suspense>
  );
}
