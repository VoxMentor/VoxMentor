"use client";

export default function FetchError({
  message,
  onRetry,
}: {
  message: string;
  onRetry: () => void;
}) {
  return (
    <div className="card flex items-center justify-between gap-4">
      <p className="text-sm text-danger">{message}</p>
      <button
        onClick={onRetry}
        className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer"
      >
        Retry
      </button>
    </div>
  );
}
