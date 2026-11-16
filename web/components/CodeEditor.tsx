"use client";

import { Editor } from "@monaco-editor/react";

interface CodeEditorProps {
  language?: string;
  value?: string;
  onChange?: (code: string) => void;
  height?: string;
}

/**
 * Monaco-powered code editor with dark theme.
 * Renders in the right (40%) panel of the practice layout. Accepts a language
 * prop for syntax highlighting and exposes value changes via onChange.
 *
 * Monaco needs a definite height. `height="100%"` resolves against an auto-height
 * parent as circular: Monaco grows to fit its content, automaticLayout re-measures
 * the taller box, grows again — forever. Below `lg` the card pins 70vh; from `lg`
 * up the practice grid row is `minmax(0, 1fr)`, so `flex-1 min-h-0` fills it.
 */
export default function CodeEditor({
  language = "javascript",
  value = "",
  onChange,
  height = "100%",
}: CodeEditorProps) {
  return (
    <div className="card flex flex-col min-w-0 h-[70vh] lg:h-auto lg:flex-1 lg:min-h-0">
      <div className="flex items-center justify-between border-b border-border px-4 py-3 bg-bg-light rounded-t-xl">
        <span className="text-xs font-medium text-text-muted">
          Code editor · {language}
        </span>
      </div>
      <Editor
        language={language}
        value={value ?? ""}
        onChange={(newValue) => onChange?.(newValue ?? "")}
        theme="vs-dark"
        options={{
          minimap: { enabled: false },
          fontSize: 14,
          lineNumbers: "on",
          wordWrap: "on",
          scrollBeyondLastLine: false,
          automaticLayout: true,
        }}
        width="100%"
        height={height}
      />
    </div>
  );
}
