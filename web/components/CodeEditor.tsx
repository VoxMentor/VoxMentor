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
 * Renders in the right (40%) panel of the practice layout and fills the
 * available space. Accepts a language prop for syntax highlighting and
 * exposes value changes via onChange.
 */
export default function CodeEditor({
  language = "javascript",
  value = "",
  onChange,
  height = "100%",
}: CodeEditorProps) {
  return (
    <div className="card flex flex-col min-w-0 h-full">
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
