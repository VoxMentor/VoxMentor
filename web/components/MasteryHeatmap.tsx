"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { api, ApiError, type MasteryConcept } from "@/lib/api";
import FetchError from "@/components/FetchError";

function colorFor(c: MasteryConcept): string {
  if (c.masteryProbability === null) return "bg-gray-200";
  if (c.isMastered || c.masteryProbability >= 0.85) return "bg-teal-500";
  if (c.masteryProbability > 0.7) return "bg-green-500";
  if (c.masteryProbability >= 0.3) return "bg-yellow-400";
  return "bg-red-500";
}

function percent(c: MasteryConcept): string {
  return c.masteryProbability === null
    ? "not practiced"
    : `${Math.round(c.masteryProbability * 100)}%`;
}

const legend = [
  { label: "Not practiced", cls: "bg-gray-200" },
  { label: "< 30%", cls: "bg-red-500" },
  { label: "30–70%", cls: "bg-yellow-400" },
  { label: "> 70%", cls: "bg-green-500" },
  { label: "Mastered", cls: "bg-teal-500" },
];

function Skeleton() {
  return (
    <div className="card">
      <div className="h-5 w-40 bg-border rounded animate-pulse mb-4" />
      <div className="grid grid-cols-5 sm:grid-cols-10 gap-2">
        {Array.from({ length: 50 }).map((_, i) => (
          <div key={i} className="aspect-square bg-border rounded-lg animate-pulse" />
        ))}
      </div>
    </div>
  );
}

export default function MasteryHeatmap() {
  const router = useRouter();
  const [concepts, setConcepts] = useState<MasteryConcept[] | null>(null);
  const [masteredCount, setMasteredCount] = useState(0);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    api
      .mastery()
      .then((profile) => {
        setConcepts(profile.concepts);
        setMasteredCount(profile.masteredCount);
        setTotal(profile.totalConcepts);
      })
      .catch((e) =>
        setError(e instanceof ApiError ? e.message : "Failed to load mastery data.")
      )
      .finally(() => setLoading(false));
  }, []);

  const retry = () => {
    setLoading(true);
    setError(null);
    load();
  };

  useEffect(() => {
    load();
  }, [load]);

  if (loading) return <Skeleton />;
  if (error) return <FetchError message={error} onRetry={retry} />;
  if (!concepts) return null;

  return (
    <div className="card">
      <div className="flex items-center justify-between mb-4">
        <h2 className="font-heading font-semibold text-navy text-lg">
          Concept Mastery
        </h2>
        <span className="text-sm text-text-muted">
          {masteredCount} / {total} mastered
        </span>
      </div>

      <div className="grid grid-cols-5 sm:grid-cols-10 gap-2">
        {concepts.map((c) => (
          <div
            key={c.conceptId}
            className="relative group"
            onClick={() => router.push(`/practice?concept=${c.conceptId}`)}
            role="button"
            tabIndex={0}
            onKeyDown={(e) => e.key === "Enter" && router.push(`/practice?concept=${c.conceptId}`)}
            aria-label={`${c.name}: ${percent(c)}. Practice this concept.`}
          >
            <button
              className={`aspect-square rounded-lg ${colorFor(c)} hover:scale-110 hover:ring-2 hover:ring-primary transition-transform cursor-pointer w-full h-full`}
              aria-hidden="true"
            />
            <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 px-2 py-1 text-xs text-white bg-navy rounded opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap pointer-events-none z-10">
              {c.name} — {percent(c)}
            </div>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap gap-x-4 gap-y-1 mt-4">
        {legend.map((l) => (
          <span key={l.label} className="flex items-center gap-1.5 text-xs text-text-muted">
            <span className={`w-3 h-3 rounded ${l.cls}`} />
            {l.label}
          </span>
        ))}
      </div>
    </div>
  );
}
