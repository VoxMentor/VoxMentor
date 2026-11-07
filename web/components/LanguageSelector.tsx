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
      className="w-full text-sm rounded-[14px] border-[1.5px] border-border bg-white px-4 py-3 text-text-body focus:border-primary focus:shadow-[0_0_0_3px_rgba(74,173,219,0.12)] focus:outline-none cursor-pointer"
    >
      {LANGUAGES.map((lang) => (
        <option key={lang.value} value={lang.value}>
          {lang.label}
        </option>
      ))}
    </select>
  );
}