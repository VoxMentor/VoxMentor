"use client";

import { type MasteryConcept } from "@/lib/api";
import MasteryProgressBar from "@/components/MasteryProgressBar";

interface ConceptCardProps {
  concept: MasteryConcept;
  problemsSolved?: number;
}

const MASTERED_THRESHOLD = 0.85;

/**
 * Per-concept mastery card: concept name, mastery %, "Mastered" badge
 * (>=0.85), problems solved count, and a color-coded progress bar.
 * Renders its own percentage and badge. MasteryProgressBar only draws the
 * bar, so nothing is duplicated.
 */
export default function ConceptCard({
  concept,
  problemsSolved,
}: ConceptCardProps) {
  const mastery = concept.masteryProbability ?? 0;
  const pct = Math.round(mastery * 100);
  const isMastered = mastery >= MASTERED_THRESHOLD || concept.isMastered;
  const solved = problemsSolved ?? concept.correctAttempts;

  return (
    <div className="flex flex-col gap-2.5 rounded-[10px] border border-border p-3">
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <h3 className="truncate text-sm font-medium text-navy">
            {concept.name}
          </h3>
          <p className="text-xs text-text-muted">
            {concept.masteryProbability === null
              ? "Not practiced"
              : `${pct}% mastered`}
          </p>
        </div>
        {isMastered && (
          <span className="shrink-0 rounded-md bg-accent-light-blue px-2 py-0.5 text-xs font-medium text-navy">
            Mastered
          </span>
        )}
      </div>
      <div className="flex items-center gap-3">
        <MasteryProgressBar mastery={concept.masteryProbability} height="6px" label={concept.name} />
        <span className="w-12 shrink-0 text-right text-sm font-medium text-text-body">
          {concept.masteryProbability === null ? "n/a" : `${pct}%`}
        </span>
      </div>
      <div className="flex items-center justify-between text-xs text-text-muted border-t border-border pt-2">
        <span>Correct attempts</span>
        <span className="font-medium text-text-body">{solved}</span>
      </div>
    </div>
  );
}
