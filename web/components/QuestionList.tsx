"use client";

import type { StudentQuestion } from "@/lib/api";

export default function QuestionList({
  questions,
  selectedId,
  onSelect,
  emptyMessage = "No questions match these filters.",
  onClearFilters,
}: {
  questions: StudentQuestion[];
  selectedId?: string;
  onSelect: (id: string) => void;
  emptyMessage?: string;
  onClearFilters?: () => void;
}) {
  if (questions.length === 0) {
    return (
      <div className="card !p-6 text-center">
        <p className="text-sm text-text-muted mb-3">{emptyMessage}</p>
        {onClearFilters && (
          <button
            type="button"
            onClick={onClearFilters}
            className="text-sm font-medium text-primary hover:underline cursor-pointer"
          >
            Clear filters
          </button>
        )}
      </div>
    );
  }

  return (
    <ul className="flex flex-col gap-1 overflow-y-auto max-h-64 lg:max-h-[calc(100vh-14rem)] -mx-1 px-1">
      {questions.map((q) => {
        const selected = q.id === selectedId;
        return (
          <li key={q.id}>
            <button
              type="button"
              onClick={() => onSelect(q.id)}
              aria-current={selected ? "true" : undefined}
              className={`w-full text-left rounded-xl px-3 py-2.5 transition-colors duration-200 cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${
                selected
                  ? "bg-accent-light-blue ring-1 ring-primary/30"
                  : "hover:bg-accent-light-blue/60"
              }`}
            >
              <span
                className={`block text-sm font-medium leading-snug ${
                  selected ? "text-navy" : "text-text-body"
                }`}
              >
                {q.title}
              </span>
              <span className="mt-1 flex flex-wrap items-center gap-1.5">
                <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-accent-light-blue text-primary">
                  {q.conceptName}
                </span>
                <span className="text-xs text-text-muted">Difficulty {q.difficulty}/10</span>
              </span>
            </button>
          </li>
        );
      })}
    </ul>
  );
}
