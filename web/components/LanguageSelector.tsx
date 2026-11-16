"use client";

interface LanguageSelectorProps {
  value: string;
  onChange: (language: string) => void;
}

const LANGUAGES: { value: string; label: string }[] = [
  { value: "python", label: "Python" },
  { value: "java", label: "Java" },
  { value: "cpp", label: "C++" },
  { value: "javascript", label: "JavaScript" },
  { value: "csharp", label: "C#" },
  { value: "c", label: "C" },
];

export default function LanguageSelector({
  value,
  onChange,
}: LanguageSelectorProps) {
  return (
    <select
      id="language-selector"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="field"
    >
      {LANGUAGES.map((lang) => (
        <option key={lang.value} value={lang.value}>
          {lang.label}
        </option>
      ))}
    </select>
  );
}
