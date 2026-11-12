"use client";

import { type Citation } from "@/lib/api";

interface TutorCitationProps {
  citations: Citation[];
}

/**
 * Expandable citation sections showing source chunks below a tutor answer.
 * Renders nothing when no citations are provided.
 */
export default function TutorCitation({ citations }: TutorCitationProps) {
  if (!citations || citations.length === 0) return null;

  const relevancePercent = (c: Citation) =>
    Math.round((c.relevance ?? 0) * 100);

  return (
    <div className="mt-4 pt-4 border-t border-border">
      <div className="flex items-center justify-between mb-2">
        <span className="text-[10px] font-semibold text-text-muted uppercase tracking-wider">
          Sources ({citations.length})
        </span>
      </div>
      <div className="space-y-2">
        {citations.map((c, idx) => (
          <details
            key={idx}
            className="group p-3 rounded-[10px] bg-white border border-[1px] border-border shadow-sm"
          >
            <summary className="flex items-center justify-between cursor-pointer list-none">
              <span className="flex items-center gap-2">
                {/* visible disclosure indicator: rotates open/closed (native marker hidden) */}
                <svg
                  width="10"
                  height="10"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  className="text-text-muted transition-transform group-open:rotate-90"
                  aria-hidden="true"
                >
                  <path d="M9 6l6 6-6 6" />
                </svg>
                <span className="text-[10px] font-medium text-primary">
                  {c.source}
                </span>
              </span>
              <span className="text-[10px] text-text-muted">
                {relevancePercent(c)}% match
              </span>
            </summary>
            <p className="mt-2 text-xs text-text-body italic leading-relaxed">
              {c.chunkText}
            </p>
          </details>
        ))}
      </div>
    </div>
  );
}
