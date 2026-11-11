"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, ApiError, type AdminPlagiarismSubmission } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { PLATFORM_ADMINS } from "@/lib/admin-nav";
import FetchError from "@/components/FetchError";

const PAGE_SIZE = 20;

export default function AdminPlagiarismPage() {
  return (
    <Suspense fallback={<PlagiarismSkeleton />}>
      <PlagiarismList />
    </Suspense>
  );
}

function PlagiarismList() {
  const { user } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();

  const pageParam = parseInt(searchParams.get("page") ?? "1", 10);
  const page = Number.isFinite(pageParam) && pageParam > 1 ? pageParam : 1;

  const isPlatformAdmin =
    !!user && user.roles.some((role) => PLATFORM_ADMINS.includes(role));

  const [submissions, setSubmissions] = useState<AdminPlagiarismSubmission[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadSubmissions = useCallback(() => {
    setLoading(true);
    setError(null);
    api
      .adminPlagiarismSessions(page, PAGE_SIZE)
      .then((result) => {
        setSubmissions(result.submissions);
        setTotal(result.total);
        setLoading(false);
      })
      .catch((e) => {
        setError(e instanceof ApiError ? e.message : "Failed to load submissions.");
        setLoading(false);
      });
  }, [page]);

  // Wrapped in setTimeout so setState (via the .then chain) isn't invoked
  // directly inside the effect body, mirroring the committed tutor-sessions pattern.
  useEffect(() => {
    setTimeout(() => {
      loadSubmissions();
    }, 0);
  }, [loadSubmissions]);

  // Issue #107 via #102: PlatformAdmin / SuperAdmin only.
  useEffect(() => {
    if (user && !isPlatformAdmin) router.replace("/dashboard");
  }, [user, isPlatformAdmin, router]);

  if (!user || !isPlatformAdmin) return null;

  const handleSessionSelect = (submissionId: string) => {
    router.push(`/admin/plagiarism/${submissionId}`);
  };

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  function goToPage(next: number) {
    router.push(next <= 1 ? "/admin/plagiarism" : `/admin/plagiarism?page=${next}`);
  }

  return (
    <div className="min-h-screen bg-bg-light">
      <main className="max-w-6xl mx-auto px-6 py-10">
        <div className="mb-8">
          <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
            Plagiarism Review
          </h1>
          <p className="text-text-muted mt-2 max-w-2xl">
            Review flagged submissions across the platform.
          </p>
        </div>

        {loading && (
          <div className="card p-4 flex items-center justify-center">
            <div className="h-8 w-24 bg-border rounded animate-pulse" />
          </div>
        )}

        {error && <FetchError message={error} onRetry={loadSubmissions} />}

        {!loading && !error && (
          <div className="flex flex-col gap-4">
            {submissions.length === 0 ? (
              <div className="card p-8 text-center text-text-muted">
                No flagged submissions yet.
              </div>
            ) : (
              <>
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-border text-left">
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        Submission
                      </th>
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        Title
                      </th>
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        User
                      </th>
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        Score
                      </th>
                      <th className="py-3 px-4 text-xs font-medium text-text-muted">
                        Updated
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {submissions.map((submission) => (
                      <tr
                        key={submission.submissionId}
                        onClick={() => handleSessionSelect(submission.submissionId)}
                        className="border-b border-border cursor-pointer transition-colors hover:bg-accent-light-blue"
                      >
                        <td className="py-3 px-4 text-sm text-text-body">
                          {submission.submissionId}
                        </td>
                        <td className="py-3 px-4 text-sm text-text-body">
                          {submission.title}
                        </td>
                        <td className="py-3 px-4 text-sm text-text-body">
                          {submission.userId}
                        </td>
                        <td className="py-3 px-4 text-sm text-text-body">
                          {Math.round(submission.similarityScore * 100)}%
                        </td>
                        <td className="py-3 px-4 text-sm text-text-muted">
                          {new Date(submission.updatedAt).toLocaleDateString(
                            undefined,
                            { month: "short", day: "numeric", year: "numeric" }
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <nav className="flex items-center justify-between mt-4" aria-label="Pagination">
                  <button
                    onClick={() => goToPage(page - 1)}
                    disabled={page === 1}
                    className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
                  >
                    Previous
                  </button>
                  <span className="text-sm text-text-muted">
                    Page {page} of {totalPages}
                  </span>
                  <button
                    onClick={() => goToPage(page + 1)}
                    disabled={page === totalPages}
                    className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
                  >
                    Next
                  </button>
                </nav>
              </>
            )}
          </div>
        )}
      </main>
    </div>
  );
}

function PlagiarismSkeleton() {
  return (
    <div className="card" aria-hidden="true">
      <div className="space-y-5">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="space-y-2">
            <div className="h-4 w-40 bg-border rounded animate-pulse" />
            <div className="h-3 w-64 bg-border rounded animate-pulse" />
          </div>
        ))}
      </div>
    </div>
  );
}
