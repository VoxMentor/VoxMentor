"use client";

export default function SkeletonLoader() {
  return (
    <div className="min-h-screen bg-bg-light flex items-center justify-center p-4">
      <div className="w-full max-w-sm space-y-4">
        <div className="h-8 bg-border rounded animate-pulse w-2/3 mx-auto" />
        <div className="h-4 bg-border rounded animate-pulse w-1/2 mx-auto" />
        <div className="space-y-3 mt-8">
          <div className="h-10 bg-border rounded animate-pulse" />
          <div className="h-10 bg-border rounded animate-pulse" />
          <div className="h-12 bg-border rounded animate-pulse" />
        </div>
      </div>
    </div>
  );
}
