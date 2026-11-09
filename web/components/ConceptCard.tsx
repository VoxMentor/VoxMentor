"use client";

import MasteryProgressBar from "@/components/MasteryProgressBar";
import { type MasteryConcept } from "@/lib/api";

interface ConceptCardProps {
  concept: MasteryConcept;
  problemsSolved?: number;
}

/**
 * Concept card showing mastery progress for a single concept.
 * Displays concept name, mastery progress bar, "Mastered" badge
 * (>= 0.85), and number of problems solved.
 */
export default function ConceptCard({
  concept,
  problemsSolved = concept.correctAttempts ?? 0,
}: ConceptCardProps) {
  const mastery = concept.masteryProbability ?? 0;
  const isMastered = mastery >= 0.85;
  const percentage = Math.round(mastery * 100);

  return (
    <div className="card p-4 flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-text-heading">
          {concept.name}
        </span>
        {isMastered && (
          <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-success/10 border border-success/20 text-success">
            Mastered
          </span>
        )}
      </div>
      <div className="flex flex-col gap-1">
        <MasteryProgressBar
          mastery={mastery}
          showPercentage={false}
          height="6px"
        />
        <div className="flex items-center justify-between text-xs">
          <span className="text-text-muted">{percentage}%</span>
          <span className="text-text-muted">
            Problems solved: {problemsSolved}
          </span>
        </div>
      </div>
    </div>
  );
}
