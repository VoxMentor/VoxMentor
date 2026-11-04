"use client";

import type { QuestionDetail } from "@/lib/api";

interface QuestionCardProps {
  detail: QuestionDetail;
}

/**
 * Displays a single practice question's detail: concept and difficulty badges,
 * title, description, and example input/output pairs.
 */
const badgeBase = "text-xs font-medium px-2 py-0.5 rounded-full";

export default function QuestionCard({
  detail,
}: QuestionCardProps) {
  return (
    <div className="card flex flex-col gap-4 min-w-0">
      <div className="flex flex-wrap items-center gap-2 mb-1">
        <span className={`${badgeBase} bg-accent-light-blue text-primary`}>
          {detail.conceptName}
        </span>
        <span className={`${badgeBase} bg-bg-light text-text-muted`}>
          Difficulty {detail.difficulty}/10
        </span>
        <span className={`${badgeBase} bg-bg-light text-text-muted`}>
          {detail.questionType}
        </span>
      </div>

      <h1 className="font-heading font-semibold text-navy text-xl mb-3 leading-tight">
        {detail.title}
      </h1>
      <p className="text-sm text-text-body leading-relaxed whitespace-pre-line mb-6 flex-1">
        {detail.description}
      </p>

      {detail.exampleInputs.length > 0 && (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6">
          {detail.exampleInputs.map((input, i) => (
            <div key={i} className="bg-navy-deep rounded-xl p-4 min-w-0">
              <p className="text-xs text-accent-light-blue mb-1 font-medium">
                Example input {i + 1}
              </p>
              <pre className="text-sm text-white whitespace-pre-wrap break-all">
                {input}
              </pre>
              <p className="text-xs text-accent-light-blue mt-3 mb-1 font-medium">
                Expected output
              </p>
              <pre className="text-sm text-white whitespace-pre-wrap break-all opacity-50">
                {detail.exampleOutputs[i] ?? "— no expected output —"}
              </pre>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
