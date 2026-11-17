"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, ApiError, type ReadinessProfile } from "@/lib/api";
import { severityColor } from "@/lib/severity";
import FetchError from "@/components/FetchError";

// ponytail: 80x80 ring, radius 30, 8px stroke. One geometry for both states.
const RADIUS = 30;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

function Ring({ pct, label }: { pct: number; label: string }) {
  return (
    <div className="relative h-[72px] w-[72px] shrink-0">
      <svg viewBox="0 0 80 80" className="h-full w-full -rotate-90">
        <circle
          cx="40"
          cy="40"
          r={RADIUS}
          fill="none"
          strokeWidth="8"
          className="stroke-border"
        />
        <circle
          cx="40"
          cy="40"
          r={RADIUS}
          fill="none"
          strokeWidth="8"
          strokeLinecap="round"
          strokeDasharray={CIRCUMFERENCE}
          strokeDashoffset={CIRCUMFERENCE * (1 - pct / 100)}
          className="stroke-primary transition-all duration-700"
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="font-heading text-lg font-semibold leading-none text-navy">
          {Math.round(pct)}%
        </span>
        <span className="mt-1 text-[10px] leading-none text-text-muted">{label}</span>
      </div>
    </div>
  );
}

function Skeleton() {
  return (
    <div className="panel">
      <div className="mb-4 h-5 w-36 animate-pulse rounded bg-border" />
      <div className="flex items-center gap-5">
        <div className="h-[72px] w-[72px] animate-pulse rounded-full bg-border" />
        <div className="flex-1 space-y-2">
          <div className="h-3 w-28 animate-pulse rounded bg-border" />
          <div className="h-3 w-24 animate-pulse rounded bg-border" />
        </div>
      </div>
    </div>
  );
}

export default function ReadinessOverview() {
  const [data, setData] = useState<ReadinessProfile | null>(null);
  const [noJd, setNoJd] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    api
      .readiness()
      .then(setData)
      .catch((e) => {
        if (e instanceof ApiError && e.status === 404) setNoJd(true);
        else setError(e instanceof ApiError ? e.message : "Failed to load readiness.");
      })
      .finally(() => setLoading(false));
  }, []);

  const retry = () => {
    setLoading(true);
    setError(null);
    setNoJd(false);
    load();
  };

  useEffect(() => {
    load();
  }, [load]);

  if (loading) return <Skeleton />;
  if (error) return <FetchError message={error} onRetry={retry} />;

  if (noJd) {
    return (
      <div className="panel">
        <h2 className="font-heading text-base font-semibold text-navy">
          Interview readiness
        </h2>
        <div className="mt-4 flex items-center gap-5">
          <Ring pct={0} label="no JD" />
          <div className="min-w-0">
            <p className="text-sm leading-relaxed text-text-body">
              Add a job description to score your readiness and see the gaps that
              matter for that role.
            </p>
            <Link
              href="/jd"
              className="mt-2 inline-block text-sm font-medium text-primary hover:text-primary-dark"
            >
              Add a job description
            </Link>
          </div>
        </div>
      </div>
    );
  }

  if (!data) return null;

  const pct = data.maxScore > 0 ? (data.score / data.maxScore) * 100 : 0;
  const topGaps = [...data.gaps].sort((a, b) => b.severity - a.severity).slice(0, 3);
  const weeks = `${data.estimatedWeeksToReady} ${
    data.estimatedWeeksToReady === 1 ? "week" : "weeks"
  }`;

  return (
    <div className="panel">
      <h2 className="font-heading text-base font-semibold text-navy">
        Interview readiness
      </h2>

      <div className="mt-4 flex items-center gap-5">
        <Ring pct={pct} label="ready" />
        <div className="min-w-0">
          <p className="text-sm text-text-body">
            {Math.round(data.score)} of {data.maxScore} points
          </p>
          <p className="mt-1 text-xs text-text-muted">{weeks} to ready</p>
        </div>
      </div>

      {topGaps.length === 0 ? (
        <p className="mt-5 text-sm text-text-muted">No significant gaps.</p>
      ) : (
        <div className="mt-5">
          <p className="mb-2 text-xs font-medium text-text-muted">Top gaps</p>
          <div className="space-y-2.5">
            {topGaps.map((gap) => (
              <div key={gap.topic} className="flex items-start gap-2.5">
                <span
                  className={`mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${severityColor(gap.severity)}`}
                />
                <div className="min-w-0">
                  <p className="text-sm text-navy">
                    {gap.topic}
                    <span className="ml-1.5 text-xs text-text-muted">
                      {Math.round(gap.severity * 100)}%
                    </span>
                  </p>
                  <p className="text-xs leading-snug text-text-muted">
                    {gap.recommendation}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
