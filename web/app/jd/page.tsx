"use client";

import { Suspense, useEffect, useRef, useState, type ReactNode } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { api, ApiError, type JdDetail } from "@/lib/api";
import { severityColor } from "@/lib/severity";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";
import SkeletonLoader from "@/components/SkeletonLoader";

const MIN_CHARS = 50;
const MAX_CHARS = 20000;

const secondaryButtonClass =
  "cursor-pointer rounded-[10px] border border-border bg-bg-card px-3 py-2 text-sm font-medium text-navy transition-colors hover:bg-bg-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy";

function clampPct(value: number): number {
  return Math.max(0, Math.min(100, value * 100));
}

function PageSkeleton() {
  return (
    <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
      <div className="panel flex flex-col gap-4">
        <div className="h-6 w-48 rounded bg-border animate-pulse" />
        <div className="min-h-64 rounded-[10px] bg-border animate-pulse" />
        <div className="h-9 w-40 rounded-[10px] bg-border animate-pulse" />
      </div>
      <div className="panel flex flex-col gap-4">
        <div className="h-5 w-36 rounded bg-border animate-pulse" />
        {Array.from({ length: 3 }, (_, i) => (
          <div key={i} className="h-12 rounded-md bg-border animate-pulse" />
        ))}
      </div>
    </div>
  );
}

function WeightCard({
  title,
  subtitle,
  skills,
}: {
  title: string;
  subtitle: string;
  skills: Record<string, number>;
}) {
  const entries = Object.entries(skills).sort((a, b) => b[1] - a[1]);

  return (
    <div className="panel">
      <h2 className="text-sm font-medium text-navy">{title}</h2>
      <p className="mt-1 text-xs text-text-muted">{subtitle}</p>

      {entries.length === 0 ? (
        <p className="mt-4 text-sm text-text-muted">
          No skills recognised in this category.
        </p>
      ) : (
        <ul className="mt-4 space-y-3">
          {entries.map(([topic, weight]) => (
            <li key={topic}>
              <div className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 break-words text-sm text-text-body">
                  {topic}
                </span>
                <span className="shrink-0 font-mono text-xs tabular-nums text-navy">
                  {Math.round(weight * 100)}%
                </span>
              </div>
              <div className="mt-1.5 h-1 overflow-hidden rounded-full bg-border">
                <div
                  className="h-full rounded-full bg-navy"
                  style={{ width: `${clampPct(weight)}%` }}
                />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function JdDetailView({ jd, onReset }: { jd: JdDetail; onReset: () => void }) {
  const gaps = [...jd.gaps].sort((a, b) => b.severity - a.severity);
  const weeks = `${jd.estimatedWeeks} ${jd.estimatedWeeks === 1 ? "week" : "weeks"}`;

  return (
    <div className="flex flex-col gap-5">
      {/* Role dossier */}
      <div className="panel flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 font-mono text-xs text-text-muted">
            <span>{jd.difficulty}</span>
            <span>{weeks} to ready</span>
            <span>{new Date(jd.createdAt).toLocaleDateString()}</span>
          </div>
          <h2 className="mt-2 break-words font-heading text-2xl font-semibold leading-tight text-navy">
            {jd.role}
          </h2>
          <p className="mt-1 break-words text-sm text-text-body">{jd.companyName}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button onClick={onReset} className={secondaryButtonClass}>
            Upload another
          </button>
          <Link
            href={`/interview?jd=${encodeURIComponent(jd.jdId)}`}
            className="btn-solid"
          >
            Practise this role
          </Link>
        </div>
      </div>

      {/* Readiness figure */}
      <div className="panel">
        <div className="flex flex-wrap items-end justify-between gap-6">
          <div>
            <p className="text-xs font-medium text-text-muted">Interview readiness</p>
            <div className="mt-1 flex items-baseline gap-1.5">
              <span className="font-heading text-5xl font-semibold leading-none tabular-nums text-navy">
                {Math.round(jd.readinessScore)}
              </span>
              <span className="font-mono text-sm text-text-muted">/ 100</span>
            </div>
          </div>
          <div className="w-full min-w-0 max-w-md">
            <div className="h-1 overflow-hidden rounded-full bg-border">
              <div
                className="h-full rounded-full bg-navy"
                style={{ width: `${Math.max(0, Math.min(100, jd.readinessScore))}%` }}
              />
            </div>
            <div className="mt-2 flex flex-wrap justify-between gap-x-4 text-xs text-text-muted">
              <span>
                {gaps.length} {gaps.length === 1 ? "gap" : "gaps"} to close
              </span>
              <span>{weeks} to ready</span>
            </div>
          </div>
        </div>
      </div>

      {/* Weighted skill ledgers */}
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <WeightCard
          title="Technical skills"
          subtitle="Weighted from the role's requirements."
          skills={jd.technicalSkills}
        />
        <WeightCard
          title="Behavioural skills"
          subtitle="Weighted from the role's requirements."
          skills={jd.hrSkills}
        />
      </div>

      {/* Gaps */}
      <div className="panel">
        <h2 className="text-sm font-medium text-navy">Gaps to close</h2>
        {gaps.length === 0 ? (
          <p className="mt-3 text-sm text-text-muted">
            No gaps for this role. Keep practising to stay sharp.
          </p>
        ) : (
          <ul className="mt-4 flex flex-col gap-3">
            {gaps.map((gap) => (
              <li key={gap.topic} className="flex gap-3">
                <span
                  aria-hidden="true"
                  className={`mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${severityColor(gap.severity)}`}
                />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-baseline justify-between gap-x-3">
                    <p className="break-words text-sm text-navy">{gap.topic}</p>
                    <span className="shrink-0 font-mono text-xs tabular-nums text-text-muted">
                      {Math.round(gap.severity * 100)}%
                    </span>
                  </div>
                  <p className="mt-0.5 text-xs leading-relaxed text-text-muted">
                    {gap.recommendation}
                  </p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      {/* Roadmap as a timeline */}
      <div className="panel">
        <h2 className="text-sm font-medium text-navy">Study roadmap</h2>
        {jd.roadmap.length === 0 ? (
          <p className="mt-3 text-sm text-text-muted">
            No roadmap needed. There is nothing to close.
          </p>
        ) : (
          <ol className="mt-4 flex flex-col">
            {jd.roadmap.map((week) => (
              <li
                key={week.week}
                className="flex gap-4 border-t border-border py-3 first:border-t-0 first:pt-0 last:pb-0"
              >
                <span className="w-10 shrink-0 font-mono text-xs tabular-nums text-text-muted">
                  W{String(week.week).padStart(2, "0")}
                </span>
                <div className="min-w-0">
                  <p className="break-words text-sm text-navy">{week.focus}</p>
                  {week.conceptIds.length > 0 && (
                    <p className="mt-0.5 font-mono text-xs text-text-muted">
                      {week.conceptIds.length} concepts
                    </p>
                  )}
                </div>
              </li>
            ))}
          </ol>
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
  // Drop a file read that finishes after the user typed or picked another file.
  const fileReadVersion = useRef(0);
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
  if (tooLong) lengthHint = `Too long. Trim to ${MAX_CHARS} characters`;
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
    const readVersion = ++fileReadVersion.current;
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;

    if (!/\.(txt|md)$/i.test(file.name)) {
      setError(
        file.name.toLowerCase().endsWith(".pdf")
          ? "PDFs can't be read in the browser here. Open the PDF and paste its text instead."
          : "Only .txt and .md files are supported. Paste the text instead."
      );
      return;
    }

    try {
      const contents = await file.text();
      if (readVersion !== fileReadVersion.current) return;
      setText(contents);
      setError(null);
    } catch {
      if (readVersion !== fileReadVersion.current) return;
      setError("Could not read that file. Paste the text instead.");
    }
  }

  return (
    <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
      <div className="panel">
        <div className="flex flex-wrap items-baseline justify-between gap-4">
          <h2 className="font-heading text-base font-semibold text-navy">
            Add a job description
          </h2>
          <span className="font-mono text-xs tabular-nums text-text-muted">
            {MIN_CHARS} to {MAX_CHARS} characters
          </span>
        </div>
        <p className="mt-1 text-xs text-text-muted">
          Paste the full description or load a plain-text file.
        </p>

        {error && (
          <p role="alert" className="error-banner mt-4 !mb-0">
            {error}
          </p>
        )}

        <div className="mt-5">
          <label
            htmlFor="jd-text"
            className="mb-1.5 block text-xs font-medium text-text-muted"
          >
            Job description
          </label>
          <textarea
            id="jd-text"
            rows={14}
            value={text}
            onChange={(e) => {
              fileReadVersion.current += 1;
              setText(e.target.value);
              if (error) setError(null);
            }}
            disabled={submitting}
            placeholder="Paste the full job description"
            className="field min-h-64 resize-y leading-relaxed"
          />
          <div className="mt-2 flex flex-wrap justify-between gap-x-4 text-xs text-text-muted">
            <span className={lengthInvalid ? "text-danger" : undefined}>
              {lengthHint}
            </span>
            <span
              className={`font-mono tabular-nums ${tooLong ? "text-danger" : undefined}`}
            >
              {text.length} / {MAX_CHARS}
            </span>
          </div>
        </div>

        <div className="mt-5 flex flex-wrap items-center gap-3 border-t border-border pt-4">
          <button
            onClick={handleSubmit}
            disabled={submitting || tooShort || tooLong}
            className="btn-solid"
          >
            {submitting && (
              <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white" />
            )}
            {submitting ? "Analysing" : "Analyse description"}
          </button>

          <label className="cursor-pointer rounded-[10px] px-3 py-2 text-sm font-medium text-navy transition-colors hover:bg-bg-light focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-navy">
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

      {/* Explains the three real outputs, so the empty half earns its place. */}
      <aside className="panel h-fit">
        <h2 className="text-sm font-medium text-navy">What you will get</h2>
        <ol className="mt-4 flex flex-col gap-4">
          {[
            [
              "01",
              "Skill weights",
              "Technical and behavioural skills ranked by the role's requirements.",
            ],
            [
              "02",
              "Gaps to close",
              "The strongest gaps first, with a recommendation for each.",
            ],
            [
              "03",
              "Study roadmap",
              "A week-by-week plan built around those gaps.",
            ],
          ].map(([n, title, description]) => (
            <li key={n} className="flex gap-3">
              <span className="font-mono text-xs tabular-nums text-text-muted">
                {n}
              </span>
              <div className="min-w-0">
                <p className="text-sm text-navy">{title}</p>
                <p className="mt-0.5 text-xs leading-relaxed text-text-muted">
                  {description}
                </p>
              </div>
            </li>
          ))}
        </ol>
      </aside>
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
          Upload another
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
        <main className="mx-auto w-full max-w-[1400px] px-8 py-10">
          <header className="mb-8">
            <h1 className="font-heading text-2xl font-semibold text-navy">
              Job description
            </h1>
            <p className="mt-1 text-sm text-text-muted">
              Turn a job description into skill weights, gaps, and a study plan.
            </p>
          </header>

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
