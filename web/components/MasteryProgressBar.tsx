"use client";

interface MasteryProgressBarProps {
  mastery: number | null; // 0–1 probability; null when no data
  label?: string;
  height?: string;
}

/**
 * Sequential blue mastery bar: neutral when unpracticed, then light blue,
 * primary, and navy as mastery rises. Callers render their own text so the
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
    barColor = "var(--border)";
    ariaValueText = "not practiced";
  } else if (mastery < 0.4) {
    barColor = "var(--accent-light-blue)";
    ariaValueText = `${percentage}%`;
  } else if (mastery < 0.7) {
    barColor = "var(--primary)";
    ariaValueText = `${percentage}%`;
  } else {
    barColor = "var(--navy)";
    ariaValueText = `${percentage}%`;
  }

  return (
    <div
      className="w-full relative rounded-full"
      style={{ height, backgroundColor: "var(--border)" }}
    >
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
