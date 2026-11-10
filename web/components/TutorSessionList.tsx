"use client";

import { type TutorSession } from "@/lib/api";

interface TutorSessionListProps {
  sessions: TutorSession[];
  onSelect: (sessionId: string) => void;
  onNew: () => void;
}

/**
 * Session history sidebar for the tutor chat.
 * Shows last 10 sessions, click to load a previous Q&A,
 * "+ New Session" starts a fresh chat.
 */
export default function TutorSessionList({
  sessions,
  onSelect,
  onNew,
}: TutorSessionListProps) {
  const formatDate = (iso: string) => {
    const d = new Date(iso);
    const now = new Date();
    const diffMs = now.getTime() - d.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    if (diffMins < 1) return "just now";
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffMins < 1440) return `${Math.floor(diffMins / 60)}h ago`;
    return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
  };

  const sessionsList = sessions.length === 0 ? (
    <p className="text-xs text-text-muted text-center py-4">
      No sessions yet. Start by asking a question.
    </p>
  ) : (
    sessions.map((session) => {
      const title = session.title || "Untitled session";
      return (
        <button
          key={session.sessionId}
          onClick={() => onSelect(session.sessionId)}
          className="w-full text-left px-3 py-2.5 text-sm text-text-body border-[1.5px] border-border rounded-[10px] hover:bg-accent-light-blue transition-colors cursor-pointer"
        >
          <div className="flex justify-between items-start mb-1">
            <span className="font-medium text-text-heading truncate">
              {title}
            </span>
            <span className="text-[10px] text-text-muted">
              {formatDate(session.updatedAt)}
            </span>
          </div>
          <div className="flex items-center gap-2">
            <span className="text-[10px] text-text-muted">
              {session.questionCount} question
              {session.questionCount !== 1 ? "s" : ""}
            </span>
          </div>
        </button>
      );
    })
  );

  return (
    <div className="flex flex-col gap-3 h-full">
      <div className="flex items-center gap-2 mb-2">
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className="text-primary"
        >
          <path d="M12 2v20M4.93 4.93l2.83 2.83M19.07 4.93l-2.83 2.83M12 12l5.83 5.83" />
          <path d="M22 12c0 4.71-2.96 8.69-7 10.11A10 10 0 0 1 2 12c0-4.71 2.96-8.69 7-10.11" />
        </svg>
        <span className="font-medium text-navy text-sm">Tutor Sessions</span>
      </div>

      <button
        onClick={onNew}
        className="w-full py-2 px-3 text-sm text-navy bg-white border-[1.5px] border-border rounded-[12px] hover:bg-accent-light-blue transition-colors cursor-pointer flex items-center justify-between"
      >
        <span>+ New Session</span>
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <line x1="12" x2="12" y1="5" y2="19" />
          <line x1="5" x2="19" y1="12" y2="12" />
        </svg>
      </button>

      <div className="flex-1 overflow-y-auto space-y-1">
        {sessionsList}
      </div>
    </div>
  );
}
