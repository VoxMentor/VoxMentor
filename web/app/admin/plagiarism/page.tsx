"use client";

import { Suspense, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import {
  api,
  ApiError,
  SUBMISSION_STATUS_LABELS,
  type AdminPlagiarismQueueItem,
} from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { PLATFORM_ADMINS } from "@/lib/admin-nav";
import FetchError from "@/components/FetchError";

const PAGE_SIZE = 20;

export default function AdminPlagiarismPage() {
  // #102: shell guard + Navbar + sidebar come from app/admin/layout.tsx.
  return (
    <Suspense fallback={<PlagiarismSkeleton />}>
      <PlagiarismContent />
    </Suspense>
  );
}

type ListState =
  | { key: string; ok: true; items: AdminPlagiarismQueueItem[] }
  | { key: string; ok: false; message: string };

function PlagiarismContent() {
  const { user, setUser } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();

  const pageParam = parseInt(searchParams.get("page") ?? "1", 10);
  const page = Number.isFinite(pageParam) && pageParam > 1 ? pageParam : 1;

  const [list, setList] = useState<ListState | null>(null);
  const [reload, setReload] = useState(0);

  // Defense-in-depth: shell admits any admin role, this page is
  // PlatformAdmin/SuperAdmin only per the ManagePlatform policy.
  const isPlatformAdmin =
    !!user && user.roles.some((role) => PLATFORM_ADMINS.includes(role));

  useEffect(() => {
    if (user && !isPlatformAdmin) router.replace("/dashboard");
  }, [user, isPlatformAdmin, router]);

  // keyed results: only the request for the current page/reload can update
  // state, so a slow response for a previous page can't overwrite this one.
  const key = `${page}|${reload}`;
  const state = list?.key === key ? list : null;
  const loading = state === null;
  const loadError = state && !state.ok ? state.message : null;
  const items = state?.ok ? state.items : [];

  useEffect(() => {
    if (!isPlatformAdmin) return;
    let cancelled = false;
    api
      .adminPlagiarismSubmissions(page, PAGE_SIZE)
      .then((submissions) => {
        if (cancelled) return;
        setList({ key, ok: true, items: submissions });
      })
      .catch((e) => {
        if (cancelled) return;
        // stale local auth (expired session or server-side demotion): re-verify
        // instead of a dead-end retry — me() 401s -> setUser(null) -> AuthProvider
        // routes to /login; a demoted caller gets refreshed roles and the guard reroutes.
        if (e instanceof ApiError && (e.status === 401 || e.status === 403)) {
          api
            .me()
            .then((me) => {
              if (cancelled) return;
              setUser(me);
              // refresh didn't explain the failure: surface retry instead of a
              // stuck skeleton
              if (me.roles.some((role) => PLATFORM_ADMINS.includes(role))) {
                setList({ key, ok: false, message: e.message });
              }
            })
            .catch(() => {
              if (!cancelled) setUser(null);
            });
          return;
        }
        setList({
          key,
          ok: false,
          message:
            e instanceof ApiError ? e.message : "Failed to load submissions.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [isPlatformAdmin, page, key, setUser]);

  function goToPage(next: number) {
    router.push(next <= 1 ? "/admin/plagiarism" : `/admin/plagiarism?page=${next}`);
  }

  if (!user || !isPlatformAdmin) return null;

  return (
    <>
      <div className="mb-6">
        <h1 className="text-2xl font-heading font-bold text-text-heading">
          Plagiarism review
        </h1>
        <p className="text-sm text-text-muted mt-1">
          Flagged submissions, highest similarity first.
        </p>
      </div>

      {loading && <PlagiarismSkeleton />}
      {loadError && (
        <FetchError message={loadError} onRetry={() => setReload((n) => n + 1)} />
      )}

      {state?.ok && (
        <>
          {items.length === 0 ? (
            <div className="card p-8 text-center">
              <p className="text-sm text-text-muted">
                {page > 1
                  ? "No submissions on this page."
                  : "No flagged submissions yet."}
              </p>
            </div>
          ) : (
            <div className="card overflow-hidden">
              <table className="w-full text-left border-collapse">
                <caption className="sr-only">
                  Flagged submissions, highest similarity first
                </caption>
                <thead>
                  <tr className="border-b border-border">
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">
                      Submission
                    </th>
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">
                      User
                    </th>
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">
                      Score
                    </th>
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">
                      Status
                    </th>
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">
                      Created
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {items.map((item) => (
                    <tr
                      key={item.id}
                      onClick={() => router.push(`/admin/plagiarism/${item.id}`)}
                      className="hover:bg-accent-light-blue transition-colors cursor-pointer"
                    >
                      <td className="px-4 py-3 text-sm text-text-body">
                        {/* keyboard-accessible primary action; the row click is a pointer shortcut */}
                        <Link
                          href={`/admin/plagiarism/${item.id}`}
                          onClick={(e) => e.stopPropagation()}
                          className="text-primary hover:text-primary-dark underline"
                        >
                          {item.id}
                        </Link>
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body truncate max-w-[200px]">
                        {item.userEmail ?? item.userId}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body">
                        {item.plagiarismScore != null
                          ? `${Math.round(item.plagiarismScore * 100)}%`
                          : "—"}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body">
                        {SUBMISSION_STATUS_LABELS[item.status] ?? String(item.status)}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-muted">
                        {new Date(item.createdAt).toLocaleDateString(undefined, {
                          month: "short",
                          day: "numeric",
                          year: "numeric",
                        })}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Pagination renders for empty pages too (e.g. out-of-range page),
              so Previous can bring the user back. */}
          <nav className="flex items-center justify-between mt-4 px-4" aria-label="Pagination">
            <button
              onClick={() => goToPage(page - 1)}
              disabled={page === 1}
              className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
            >
              Previous
            </button>
            <span className="text-sm text-text-muted">Page {page}</span>
            {/* ponytail: API returns the page only, no totalCount — Next is
                a full-page heuristic like /admin/audit; an exact multiple of
                PAGE_SIZE can land on one empty page. Add totals if needed. */}
            <button
              onClick={() => goToPage(page + 1)}
              disabled={items.length < PAGE_SIZE}
              className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
            >
              Next
            </button>
          </nav>
        </>
      )}
    </>
  );
}

function PlagiarismSkeleton() {
  return (
    <div className="card p-8" aria-hidden="true">
      <div className="space-y-4">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="flex items-center gap-4">
            <div className="h-10 w-40 bg-border rounded animate-pulse" />
            <div className="h-10 w-32 bg-border rounded animate-pulse" />
            <div className="h-10 w-48 bg-border rounded animate-pulse" />
            <div className="h-10 w-28 bg-border rounded animate-pulse" />
            <div className="h-10 w-12 bg-border rounded animate-pulse" />
          </div>
        ))}
      </div>
    </div>
  );
}
