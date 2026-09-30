"use client";

function ChevronLeft() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true">
      <path d="m15 18-6-6 6-6" />
    </svg>
  );
}

function ChevronRight() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true">
      <path d="m9 18 6-6-6-6" />
    </svg>
  );
}

export default function QuestionNavigation({
  onPrev,
  onNext,
  hasPrev,
  hasNext,
  position,
  total,
}: {
  onPrev: () => void;
  onNext: () => void;
  hasPrev: boolean;
  hasNext: boolean;
  position: number;
  total: number;
}) {
  const navBtn =
    "inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium rounded-xl border transition-colors duration-200 cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50";
  return (
    <div className="flex items-center justify-between gap-3">
      <button
        type="button"
        onClick={onPrev}
        disabled={!hasPrev}
        className={`${navBtn} border-border text-text-body hover:border-primary hover:text-primary`}
      >
        <ChevronLeft />
        Prev
      </button>
      <span className="text-sm text-text-muted tabular-nums" aria-live="polite">
        Question {position} of {total}
      </span>
      <button
        type="button"
        onClick={onNext}
        disabled={!hasNext}
        className={`${navBtn} border-border text-text-body hover:border-primary hover:text-primary`}
      >
        Next
        <ChevronRight />
      </button>
    </div>
  );
}
