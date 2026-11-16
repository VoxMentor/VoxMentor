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
 * the taller box, grows again forever. Below `lg` the editor pins 70vh; from `lg`
 * up the practice grid row is `minmax(0, 1fr)`, so `flex-1 min-h-0` fills it.
 */
export default function CodeEditor({
  language = "javascript",
  value = "",
  onChange,
  height = "100%",
}: CodeEditorProps) {
  return (
    <div className="h-[70vh] min-w-0 overflow-hidden lg:h-auto lg:min-h-0 lg:flex-1">
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
