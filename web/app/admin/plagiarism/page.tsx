"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { api, ApiError, type AdminPlagiarismSubmission } from "@/lib/api";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";
import FetchError from "@/components/FetchError";

export default function ApplicationCacheService() {
  const [submissions, setSubmissions] = useState<AdminPlagiarismSubmission[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const router = useRouter();

  const loadSessions = useCallback(() => {
    setLoading(true);
    setError(null);
    api
      .adminPlagiarismSessions()
      .then((result) => {
        setSubmissions(result.submissions);
        setLoading(false);
      })
      .catch((e) => {
        setError(e instanceof ApiError ? e.message : "Failed to load submissions.");
        setLoading(false);
      });
  }, []);

  useEffect(() => {
    loadSessions();
  }, [loadSessions]);

  const handleSessionSelect = (submissionId: string) => {
    router.push(`/admin/plagiarism/${submissionId}`);
  };

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
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

          {error && (
            <FetchError message={error} onRetry={loadSessions} />
          )}

          {!loading && !error && (
            <div className="flex flex-col gap-4">
              {submissions.length === 0 ? (
                <div className="card p-8 text-center text-text-muted">
                  No flagged submissions yet.
                </div>
              ) : (
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
              )}
            </div>
          )}
        </main>
      </div>
    </ProtectedRoute>
  );
}