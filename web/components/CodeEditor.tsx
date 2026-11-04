"use client";

interface CodeEditorProps {
  language?: string;
}

/**
 * Placeholder for the code editor (Monaco integration in the next issue).
 * Renders a card reserving the editor column of the practice layout.
 */
export default function CodeEditor({
  language = "javascript",
}: CodeEditorProps) {
  return (
    <div className="card flex flex-col min-w-0">
      <div className="flex items-center justify-between border-b border-border px-4 py-3 bg-bg-light rounded-t-xl">
        <span className="text-xs font-medium text-text-muted">
          Code editor · {language}
        </span>
        <span className="text-[10px] font-medium text-text-muted tracking-wider uppercase">
          Monaco integration — next issue
        </span>
      </div>
      <div className="flex-1 p-4 min-h-[300px] flex flex-col items-center justify-center text-text-muted">
        <span className="text-sm">Code editor placeholder.</span>
        <p className="text-xs mt-1 max-w-xs text-center">
          Monaco editor integration lands in the next issue. This area reserves
          the 40% editor column of the practice layout.
        </p>
      </div>
    </div>
  );
}
