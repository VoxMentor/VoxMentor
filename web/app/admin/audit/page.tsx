"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, ApiError, type AuditLog } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import FetchError from "@/components/FetchError";

const PAGE_SIZE = 20;

export default function AdminAuditPage() {
  // #102: shell guard + Navbar + sidebar come from app/admin/layout.tsx.
  return (
    <Suspense fallback={<AuditSkeleton />}>
      <AuditContent />
    </Suspense>
  );
}

type AuditState =
  | { key: string; ok: true; logs: AuditLog[] }
  | { key: string; ok: false; message: string };

function AuditContent() {
  const { user, setUser } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();

  const pageParam = parseInt(searchParams.get("page") ?? "1", 10);
  const page = Number.isFinite(pageParam) && pageParam > 1 ? pageParam : 1;

  const [list, setList] = useState<AuditState | null>(null);
  const [reload, setReload] = useState(0);

  // Defense-in-depth: shell admits any admin role, this page is
  // PlatformAdmin/SuperAdmin only per the ManagePlatform policy.
  const isPlatformAdmin =
    !!user?.roles?.includes("PlatformAdmin") ||
    !!user?.roles?.includes("SuperAdmin");

  useEffect(() => {
    if (user && !isPlatformAdmin) router.replace("/dashboard");
  }, [user, isPlatformAdmin, router]);

  const key = `${page}|${reload}`;
  const state = list?.key === key ? list : null;
  const loading = state === null;
  const loadError = state && !state.ok ? state.message : null;
  const logs = state?.ok ? state.logs : [];

  useEffect(() => {
    if (!isPlatformAdmin) return;
    let cancelled = false;
    api.adminAuditLogs(page, PAGE_SIZE)
      .then((logList) => {
        if (cancelled) return;
        setList({ key, ok: true, logs: logList });
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
              if (
                me.roles.includes("PlatformAdmin") ||
                me.roles.includes("SuperAdmin")
              ) {
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
          message: e instanceof ApiError ? e.message : "Failed to load audit logs.",
        });
      });
    return () => { cancelled = true; };
  }, [isPlatformAdmin, page, key, setUser]);

  function goToPage(next: number) {
    router.push(next <= 1 ? "/admin/audit" : `/admin/audit?page=${next}`);
  }

  if (!user || !isPlatformAdmin) return null;

  const readableDate = (iso: string) => {
    try {
      return new Date(iso).toLocaleString(undefined, {
        year: "numeric",
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });
    } catch {
      return "—";
    }
  };

  return (
    <>
      <div className="mb-6">
        <h1 className="text-2xl font-heading font-bold text-text-heading">Audit log</h1>
        <p className="text-sm text-text-muted mt-1">
          A chronological record of administrative actions.
        </p>
      </div>

      {loading && <AuditSkeleton />}
      {loadError && (
        <FetchError message={loadError} onRetry={() => setReload((n) => n + 1)} />
      )}

      {state?.ok && (
        <>
          {logs.length === 0 ? (
            <div className="card p-8 text-center">
              <p className="text-sm text-text-muted">No audit log entries found.</p>
            </div>
          ) : (
            <div className="card overflow-hidden">
              <table className="w-full text-left border-collapse">
                <caption className="sr-only">Audit log entries, newest first</caption>
                <thead>
                  <tr className="border-b border-border">
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">Timestamp</th>
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">Actor</th>
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">Action</th>
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider">Entity</th>
                    <th scope="col" className="px-4 py-3 text-xs font-semibold text-text-muted uppercase tracking-wider text-right">Id</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {logs.map((log) => (
                    <tr key={log.id} className="hover:bg-accent-light-blue transition-colors">
                      <td className="px-4 py-3 text-sm text-text-body">
                        {readableDate(log.createdAt)}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body truncate max-w-[160px]">
                        {log.userId ?? "system"}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body font-medium capitalize truncate">
                        {log.action}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body truncate max-w-[140px]">
                        <span title={log.entityId ?? "—"}>
                          {log.entity}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-sm text-text-muted text-right">{log.id}</td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <nav className="flex items-center justify-between mt-4 px-4" aria-label="Pagination">
                <button
                  onClick={() => goToPage(page - 1)}
                  disabled={page === 1}
                  className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
                >
                  Previous
                </button>
                <span className="text-sm text-text-muted">Page {page}</span>
                {/* ponytail: API returns the page only, no totalCount -- Next is
                    a full-page heuristic like /admin/users; an exact multiple of
                    PAGE_SIZE can land on one empty page. Add totals if needed. */}
                <button
                  onClick={() => goToPage(page + 1)}
                  disabled={logs.length < PAGE_SIZE}
                  className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
                >
                  Next
                </button>
              </nav>
            </div>
          )}
        </>
      )}
    </>
  );
}

function AuditSkeleton() {
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
