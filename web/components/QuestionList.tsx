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
      <div className="panel-sm">
        <p className="text-sm leading-relaxed text-text-body">{emptyMessage}</p>
        {onClearFilters && (
          <button
            type="button"
            onClick={onClearFilters}
            className="mt-2 text-sm font-medium text-navy underline underline-offset-2 cursor-pointer"
          >
            Clear filters
          </button>
        )}
      </div>
    );
  }

  return (
    <ul className="-mx-1 flex max-h-72 flex-col gap-1 overflow-y-auto px-1">
      {questions.map((q, i) => {
        const selected = q.id === selectedId;
        return (
          <li key={q.id}>
            <button
              type="button"
              onClick={() => onSelect(q.id)}
              aria-current={selected ? "true" : undefined}
              className={`flex w-full gap-2 rounded-[10px] border-l-2 px-3 py-2.5 text-left transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy ${
                selected
                  ? "border-primary bg-accent-light-blue/50"
                  : "border-transparent hover:bg-bg-light"
              }`}
            >
              <span className="w-6 shrink-0 pt-0.5 font-mono text-xs tabular-nums text-text-muted">
                {String(i + 1).padStart(2, "0")}
              </span>
              <span className="min-w-0 flex-1">
                <span
                  className={`block text-sm font-medium leading-snug ${
                    selected ? "text-navy" : "text-text-body"
                  }`}
                >
                  {q.title}
                </span>
                <span className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-text-muted">
                  <span>{q.conceptName}</span>
                  <span>{q.difficulty}/10</span>
                </span>
              </span>
            </button>
          </li>
        );
      })}
    </ul>
  );
}
