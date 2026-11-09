"use client";

interface MasteryProgressBarProps {
  mastery: number;          // 0-1 probability
  label?: string;           // concept label (shown above bar)
  showPercentage?: boolean; // whether to show percentage text inside bar
  height?: string;          // bar height, default "8px"
}

/**
 * Color-coded mastery progress bar.
 * Colors: red (<30%), yellow (30-70%), green (>70%).
 */
export default function MasteryProgressBar({
  mastery,
  label,
  showPercentage = true,
  height = "8px",
}: MasteryProgressBarProps) {
  const percentage = Math.round((mastery ?? 0) * 100);
  const isMastered = mastery !== null && mastery >= 0.85;

  const barColor =
    mastery === null
      ? "#E2E8F0" // gray when no data
      : mastery < 0.3
        ? "#EF4444" // red — danger
        : mastery < 0.7
          ? "#F5A623" // yellow — accent/gold
          : "#10B981"; // green — success

  return (
    <div className="flex flex-col gap-2">
      {label && (
        <div className="flex items-center justify-between">
          <span className="text-xs font-medium text-text-body">{label}</span>
          {showPercentage && (
            <span className="text-xs font-medium text-text-muted">
              {percentage}%
            </span>
          )}
        </div>
      )}
      <div className="w-full relative bg-gray-200 rounded-full" style={{ height }}>
        <div
          className="h-full rounded-full transition-all duration-500 ease-out"
          style={{ width: `${Math.max(percentage, 0)}%`, backgroundColor: barColor }}
        />
      </div>
      {!showPercentage && (
        <span className="text-xs font-medium text-text-muted">
          {isMastered ? "Mastered" : percentage + "%"}
        </span>
      )}
    </div>
  );
}
