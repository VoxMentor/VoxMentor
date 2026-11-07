"use client";

import dynamic from "next/dynamic";
import { useEffect, useRef } from "react";
import type * as monaco from "monaco-editor";

const Editor = dynamic(
  () => import("@monaco-editor/react").then((mod) => mod.Editor),
  { ssr: false }
);

interface CodeEditorProps {
  language: string;
  value: string;
  onChange: (value: string) => void;
  height?: string;
}

const LANGUAGE_MAP: Record<string, string> = {
  python: "python",
  java: "java",
  cpp: "cpp",
  javascript: "javascript",
  csharp: "csharp",
  c: "c",
};

export default function CodeEditor({
  language,
  value,
  onChange,
  height = "100%",
}: CodeEditorProps) {
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const monacoLanguage = LANGUAGE_MAP[language] ?? "javascript";

  useEffect(() => {
    if (editorRef.current) {
      const model = editorRef.current.getModel();
      if (model) {
        monaco.editor.setModelLanguage(model, monacoLanguage);
      }
    }
  }, [monacoLanguage]);

  return (
    <div className="card flex flex-col min-w-0">
      <div className="flex items-center justify-between border-b border-border px-4 py-3 bg-bg-light rounded-t-xl">
        <span className="text-xs font-medium text-text-muted">
          Code editor · {language}
        </span>
      </div>
      <div className="flex-1 p-0 min-h-0 relative">
        <Editor
          height={height}
          language={monacoLanguage}
          theme="vs-dark"
          value={value}
          onChange={onChange}
          options={{
            automaticLayout: true,
            minimap: { enabled: false },
            fontSize: 14,
            lineNumbers: "on",
            scrollBeyondLastLine: false,
            tabSize: 2,
            wordWrap: "on",
          }}
          onMount={(editor) => {
            editorRef.current = editor;
          }}
        />
      </div>
    </div>
  );
}