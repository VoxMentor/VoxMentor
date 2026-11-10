"use client";

interface MasteryProgressBarProps {
  mastery: number | null; // 0–1 probability; null when no data
  label?: string;
  height?: string;
}

/**
 * Color-coded mastery progress bar.
 * Red <30%, yellow 30–70%, green >70%, gray when no data.
 * The component renders only the bar — callers render their own text so the
 * percentage is never duplicated. Exposes progressbar semantics for screen
 * readers and clamps the fill to the track.
 */
export default function MasteryProgressBar({
  mastery,
  label,
  height = "8px",
}: MasteryProgressBarProps) {
  const percentage = mastery === null ? 0 : Math.round(mastery * 100);
  const width = Math.min(Math.max(percentage, 0), 100);

  let barColor: string;
  let ariaValueText: string;
  if (mastery === null) {
    barColor = "#E2E8F0"; // gray — no data
    ariaValueText = "not practiced";
  } else if (mastery < 0.3) {
    barColor = "#EF4444"; // red — danger
    ariaValueText = `${percentage}%`;
  } else if (mastery < 0.7) {
    barColor = "#F5A623"; // yellow — accent/gold
    ariaValueText = `${percentage}%`;
  } else {
    barColor = "#10B981"; // green — success
    ariaValueText = `${percentage}%`;
  }

  return (
    <div className="w-full relative bg-gray-200 rounded-full" style={{ height }}>
      <div
        role="progressbar"
        aria-valuenow={percentage}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuetext={ariaValueText}
        aria-label={label}
        className="h-full rounded-full transition-all duration-500 ease-out"
        style={{ width: `${width}%`, backgroundColor: barColor }}
      />
    </div>
  );
}
