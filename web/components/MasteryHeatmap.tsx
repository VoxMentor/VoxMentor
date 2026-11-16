"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { api, ApiError, type MasteryConcept } from "@/lib/api";
import FetchError from "@/components/FetchError";

// ponytail: one sequential ramp (neutral -> light blue -> primary -> navy) so the
// grid reads as intensity, not as five unrelated status colors.
function toneFor(c: MasteryConcept): string {
  if (c.masteryProbability === null) return "bg-border";
  if (c.isMastered || c.masteryProbability >= 0.9) return "bg-navy";
  if (c.masteryProbability >= 0.7) return "bg-navy-light";
  if (c.masteryProbability >= 0.4) return "bg-primary";
  return "bg-accent-light-blue";
}

function percent(c: MasteryConcept): string {
  return c.masteryProbability === null
    ? "not practiced"
    : `${Math.round(c.masteryProbability * 100)}% mastery`;
}

const legend = [
  { label: "Not practiced", cls: "bg-border" },
  { label: "< 40%", cls: "bg-accent-light-blue" },
  { label: "40-70%", cls: "bg-primary" },
  { label: "70-90%", cls: "bg-navy-light" },
  { label: "Mastered", cls: "bg-navy" },
];

function Skeleton() {
  return (
    <div className="panel">
      <div className="mb-6 h-5 w-40 animate-pulse rounded bg-border" />
      <div className="space-y-4">
        {Array.from({ length: 4 }).map((_, row) => (
          <div key={row} className="flex items-center gap-6">
            <div className="h-3 w-28 animate-pulse rounded bg-border" />
            <div className="flex flex-wrap gap-1.5">
              {Array.from({ length: 10 }).map((_, i) => (
                <div key={i} className="h-7 w-7 animate-pulse rounded-md bg-border" />
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/**
 * Concept mastery heatmap, grouped by curriculum category.
 * Each tile is a concept; hover or focus reports the concept in the status line
 * below the grid (no floating tooltips, so nothing overflows on mobile).
 * Clicking or pressing Enter/Space opens practice for that concept.
 */
export default function MasteryHeatmap() {
  const router = useRouter();
  const [concepts, setConcepts] = useState<MasteryConcept[] | null>(null);
  const [masteredCount, setMasteredCount] = useState(0);
  const [total, setTotal] = useState(0);
  const [active, setActive] = useState<MasteryConcept | null>(null);
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

  const groups = useMemo(() => {
    const byCategory = new Map<string, MasteryConcept[]>();
    for (const c of concepts ?? []) {
      const list = byCategory.get(c.category);
      if (list) list.push(c);
      else byCategory.set(c.category, [c]);
    }
    return [...byCategory.entries()];
  }, [concepts]);

  if (loading) return <Skeleton />;
  if (error) return <FetchError message={error} onRetry={retry} />;
  if (!concepts) return null;

  return (
    <div className="panel">
      <div className="mb-5 flex items-baseline justify-between gap-4">
        <h2 className="font-heading text-base font-semibold text-navy">
          Concept mastery
        </h2>
        <span className="text-xs text-text-muted">
          {masteredCount} of {total} mastered
        </span>
      </div>

      <div className="space-y-3">
        {groups.map(([category, items]) => (
          <div key={category} className="flex gap-4 sm:gap-6">
            <div className="w-20 shrink-0 pt-1 sm:w-32">
              <p className="text-xs leading-tight text-text-muted">{category}</p>
            </div>
            <div className="flex flex-wrap gap-1.5">
              {items.map((c) => (
                <button
                  key={c.conceptId}
                  type="button"
                  onClick={() => router.push(`/practice?concept=${c.conceptId}`)}
                  onMouseEnter={() => setActive(c)}
                  onMouseLeave={() => setActive(null)}
                  onFocus={() => setActive(c)}
                  onBlur={() => setActive(null)}
                  aria-label={`${c.name}: ${percent(c)}. Practice this concept.`}
                  className={`h-7 w-7 rounded-md ${toneFor(c)} cursor-pointer transition-shadow hover:ring-2 hover:ring-navy/30 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy`}
                />
              ))}
            </div>
          </div>
        ))}
      </div>

      <p aria-hidden="true" className="mt-5 h-4 truncate text-xs text-text-muted">
        {active ? (
          <>
            <span className="font-medium text-navy">{active.name}</span>
            {" · "}
            {percent(active)}
          </>
        ) : (
          "Hover a concept to see its name and mastery."
        )}
      </p>

      <div className="mt-4 flex flex-wrap gap-x-4 gap-y-2">
        {legend.map((l) => (
          <span key={l.label} className="flex items-center gap-1.5 text-xs text-text-muted">
            <span className={`h-3 w-3 rounded-sm ${l.cls}`} />
            {l.label}
          </span>
        ))}
      </div>
    </div>
  );
}
