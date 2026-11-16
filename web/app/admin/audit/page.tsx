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
    if (user && !isPlatformAdmin) router.replace("/admin");
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
        // instead of a dead-end retry. me() 401s -> setUser(null) -> AuthProvider
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
      return "n/a";
    }
  };

  return (
    <>
      <header className="mb-8">
        <h1 className="font-heading text-2xl font-semibold text-navy">
          Audit log
        </h1>
        <p className="mt-1 max-w-2xl text-sm text-text-muted">
          A chronological record of administrative actions.
        </p>
      </header>

      {loading && <AuditSkeleton />}
      {loadError && (
        <FetchError message={loadError} onRetry={() => setReload((n) => n + 1)} />
      )}

      {state?.ok && (
        <>
          {logs.length === 0 ? (
            <div className="panel">
              <p className="text-sm text-text-muted">No audit log entries found.</p>
            </div>
          ) : (
            <div className="panel overflow-hidden">
              <table className="w-full text-left border-collapse">
                <caption className="sr-only">Audit log entries, newest first</caption>
                <thead>
                  <tr className="border-b border-border">
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">Time</th>
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">Actor</th>
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">Action</th>
                    <th scope="col" className="whitespace-nowrap px-4 py-3 text-xs font-medium text-text-muted">Entity</th>
                    <th scope="col" className="px-4 py-3 text-xs font-medium text-text-muted text-right">Id</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {logs.map((log) => (
                    <tr key={log.id} className="transition-colors hover:bg-bg-light">
                      <td className="px-4 py-3 text-sm text-text-body tabular-nums">
                        {readableDate(log.createdAt)}
                      </td>
                      <td className="px-4 py-3 text-xs text-text-muted truncate max-w-[160px] font-mono">
                        {log.userId ?? "system"}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body capitalize truncate">
                        {log.action}
                      </td>
                      <td className="px-4 py-3 text-sm text-text-body truncate max-w-[140px]">
                        <span title={log.entityId ?? "n/a"}>
                          {log.entity}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-right font-mono text-xs tabular-nums text-text-muted">
                        {log.id}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Pagination renders for empty pages too (e.g. out-of-range page),
              so Previous can bring the user back. */}
          <nav className="flex items-center justify-between mt-4 px-1" aria-label="Pagination">
            <button
              onClick={() => goToPage(page - 1)}
              disabled={page === 1}
              className="px-3 py-1.5 text-sm text-navy rounded-[10px] border border-border cursor-pointer transition-colors hover:bg-bg-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
            >
              Previous
            </button>
            <span className="font-mono text-xs tabular-nums text-text-muted">
              Page {page}
            </span>
            {/* ponytail: API returns the page only, no totalCount -- Next is
                a full-page heuristic like /admin/users; an exact multiple of
                PAGE_SIZE can land on one empty page. Add totals if needed. */}
            <button
              onClick={() => goToPage(page + 1)}
              disabled={logs.length < PAGE_SIZE}
              className="px-3 py-1.5 text-sm text-navy rounded-[10px] border border-border cursor-pointer transition-colors hover:bg-bg-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
            >
              Next
            </button>
          </nav>
        </>
      )}
    </>
  );
}

function AuditSkeleton() {
  return (
    <div className="panel" aria-hidden="true">
      <div className="space-y-4">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="flex items-center gap-4">
            <div className="h-4 w-40 bg-border rounded animate-pulse" />
            <div className="h-4 w-32 bg-border rounded animate-pulse" />
            <div className="h-4 w-48 bg-border rounded animate-pulse" />
            <div className="h-4 w-28 bg-border rounded animate-pulse" />
            <div className="h-4 w-12 bg-border rounded animate-pulse" />
          </div>
        ))}
      </div>
    </div>
  );
}
