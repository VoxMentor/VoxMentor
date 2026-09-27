"use client";

import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type ReadinessProfile } from "@/lib/api";
import FetchError from "@/components/FetchError";

function Skeleton() {
  return (
    <div className="card">
      <div className="h-5 w-40 bg-border rounded animate-pulse mb-4" />
      <div className="flex flex-col sm:flex-row gap-6 items-center">
        <div className="w-32 h-32 rounded-full bg-border animate-pulse" />
        <div className="flex-1 w-full space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-10 bg-border rounded-xl animate-pulse" />
          ))}
        </div>
      </div>
    </div>
  );
}

function severityColor(severity: number): string {
  if (severity >= 0.5) return "bg-red-500";
  if (severity >= 0.25) return "bg-yellow-400";
  return "bg-green-500";
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
      <div className="card">
        <h2 className="font-heading font-semibold text-navy text-lg mb-2">
          Interview Readiness
        </h2>
        <p className="text-sm text-text-muted">
          Add a job description to see your readiness score, skill gaps, and
          estimated time to ready.
        </p>
      </div>
    );
  }
  if (!data) return null;

  const pct = data.maxScore > 0 ? (data.score / data.maxScore) * 100 : 0;
  const radius = 54;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference * (1 - pct / 100);
  const topGaps = [...data.gaps].sort((a, b) => b.severity - a.severity).slice(0, 5);

  return (
    <div className="card">
      <h2 className="font-heading font-semibold text-navy text-lg mb-4">
        Interview Readiness
      </h2>

      <div className="flex flex-col sm:flex-row gap-6 items-center">
        <div className="relative w-32 h-32 shrink-0">
          <svg viewBox="0 0 120 120" className="w-full h-full -rotate-90">
            <circle
              cx="60" cy="60" r={radius}
              fill="none" strokeWidth="12"
              className="stroke-border"
            />
            <circle
              cx="60" cy="60" r={radius}
              fill="none" strokeWidth="12"
              strokeLinecap="round"
              className="stroke-primary transition-all duration-700"
              strokeDasharray={circumference}
              strokeDashoffset={offset}
            />
          </svg>
          <div className="absolute inset-0 flex flex-col items-center justify-center">
            <span className="font-heading font-bold text-navy text-2xl">
              {Math.round(pct)}%
            </span>
            <span className="text-xs text-text-muted">ready</span>
          </div>
        </div>

        <div className="flex-1 w-full">
          <div className="flex items-center justify-between mb-3">
            <span className="text-sm text-text-muted">Score</span>
            <span className="text-sm font-medium text-navy">
              {Math.round(data.score)} / {data.maxScore}
            </span>
          </div>
          <div className="flex items-center justify-between mb-3">
            <span className="text-sm text-text-muted">Estimated weeks to ready</span>
            <span className="text-sm font-medium text-navy">
              {data.estimatedWeeksToReady}
            </span>
          </div>

          {topGaps.length === 0 ? (
            <p className="text-sm text-success">No significant gaps — you&apos;re set.</p>
          ) : (
            <div className="space-y-2">
              {topGaps.map((gap) => (
                <div
                  key={gap.topic}
                  className="flex items-start gap-3 p-2 bg-bg-light rounded-xl"
                >
                  <span
                    className={`mt-1.5 w-2 h-2 rounded-full shrink-0 ${severityColor(gap.severity)}`}
                  />
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-navy truncate">
                      {gap.topic}
                      <span className="ml-2 text-xs font-normal text-text-muted">
                        severity {Math.round(gap.severity * 100)}%
                      </span>
                    </p>
                    <p className="text-xs text-text-muted">{gap.recommendation}</p>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
