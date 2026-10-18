"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, ApiError, type AdminUser } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import FetchError from "@/components/FetchError";

const PAGE_SIZE = 50;

export default function AdminUsersPage() {
  // #102: guard, Navbar and sidebar come from app/admin/layout.tsx
  return (
    <Suspense fallback={<UsersSkeleton />}>
      <UsersContent />
    </Suspense>
  );
}

type UsersState =
  | { key: string; ok: true; users: AdminUser[]; roles: string[] }
  | { key: string; ok: false; message: string };

function UsersContent() {
  const { user, setUser } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();

  const pageParam = parseInt(searchParams.get("page") ?? "1", 10);
  const page = Number.isFinite(pageParam) && pageParam > 1 ? pageParam : 1;

  const [list, setList] = useState<UsersState | null>(null);
  const [reload, setReload] = useState(0);
  const [banner, setBanner] = useState<string | null>(null);
  const [busyIds, setBusyIds] = useState<ReadonlySet<string>>(() => new Set());

  const isSuperAdmin = !!user?.roles?.includes("SuperAdmin");

  // keyed results: loading/error are derived, never setState synchronously in an effect
  const key = `${page}|${reload}`;
  const state = list?.key === key ? list : null;
  const loading = state === null;
  const loadError = state && !state.ok ? state.message : null;
  const users = state?.ok ? state.users : [];
  const roles = state?.ok ? state.roles : [];

  // Deliberate guard layering (Issue #102): the admin shell admits any admin
  // role, but this page is SuperAdmin-only per the ManageRoles policy — the
  // server 403s everyone else, this redirect just keeps the UI honest.
  // Anonymous routing is AdminRoute's + AuthProvider's job.
  useEffect(() => {
    if (user && !isSuperAdmin) router.replace("/dashboard");
  }, [user, isSuperAdmin, router]);

  useEffect(() => {
    if (!isSuperAdmin) return;
    let cancelled = false;
    Promise.all([api.adminUsers(page, PAGE_SIZE), api.adminRoles()])
      .then(([userList, roleList]) => {
        if (cancelled) return;
        setList({ key, ok: true, users: userList, roles: roleList });
      })
      .catch((e) => {
        if (cancelled) return;
        // stale local auth (expired session or server-side demotion): re-verify instead
        // of a dead-end retry. me() 401s -> setUser(null) -> AuthProvider routes to
        // /login; a demoted caller gets refreshed roles and the guard reroutes.
        if (e instanceof ApiError && (e.status === 401 || e.status === 403)) {
          api
            .me()
            .then((me) => {
              if (cancelled) return;
              setUser(me);
              // refresh didn't explain the failure (e.g. claims stale vs DB):
              // surface retry instead of a stuck skeleton
              if (me.roles.includes("SuperAdmin")) {
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
          message: e instanceof ApiError ? e.message : "Failed to load users.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [isSuperAdmin, page, key, setUser]);

  function goToPage(next: number) {
    router.push(next <= 1 ? "/admin/users" : `/admin/users?page=${next}`);
  }

  async function mutate(userId: string, action: () => Promise<AdminUser>) {
    setBusyIds((prev) => new Set(prev).add(userId));
    setBanner(null);
    try {
      const updated = await action();
      // post-refresh: the response carries the row's new roles, no refetch needed
      setList((prev) =>
        prev && prev.key === key && prev.ok
          ? { ...prev, users: prev.users.map((u) => (u.id === updated.id ? updated : u)) }
          : prev
      );
      // keep useAuth in sync when the caller changed their own roles
      if (user && updated.id === user.id) {
        try {
          const me = await api.me();
          setUser(me);
          if (!me.roles.includes("SuperAdmin")) router.replace("/dashboard");
        } catch (e) {
          // the role change already succeeded; only a 401 ends the session.
          // anything else (network blip, 5xx) is transient, so stay put and warn.
          if (e instanceof ApiError && e.status === 401) setUser(null);
          else setBanner("Role updated, but refreshing your session failed. Reload the page.");
        }
      }
    } catch (e) {
      // same stale-auth handling as the load path: refresh identity so the shell
      // reroutes instead of a dead-end error banner
      if (e instanceof ApiError && (e.status === 401 || e.status === 403)) {
        api
          .me()
          .then((me) => {
            setUser(me);
            // refresh didn't explain the failure: show it instead of a silent no-op
            if (me.roles.includes("SuperAdmin")) setBanner(e.message);
          })
          .catch(() => setUser(null));
      } else {
        setBanner(e instanceof ApiError ? e.message : "Request failed.");
      }
    } finally {
      setBusyIds((prev) => {
        const next = new Set(prev);
        next.delete(userId);
        return next;
      });
    }
  }

  if (!user || !isSuperAdmin) return null;

  return (
    <>
      <div className="mb-6">
        <h1 className="text-2xl font-heading font-bold text-text-heading">Users &amp; Roles</h1>
        {state?.ok && (
          <p className="text-sm text-text-muted mt-1">
            Showing {users.length} user{users.length === 1 ? "" : "s"}, page {page}
          </p>
        )}
      </div>

      {banner && (
        <div className="error-banner" role="alert">
          {banner}
        </div>
      )}

      {loading && <UsersSkeleton />}
      {loadError && (
        <FetchError message={loadError} onRetry={() => setReload((n) => n + 1)} />
      )}

      {state?.ok && (
        <>
          <div className="card">
            {users.length === 0 ? (
              <p className="text-center text-sm text-text-muted">No users found.</p>
            ) : (
              <ul className="divide-y divide-border">
                {users.map((u) => (
                  <UserRow
                    key={u.id}
                    u={u}
                    roles={roles}
                    busy={busyIds.has(u.id)}
                    onAssign={(role) => mutate(u.id, () => api.adminAssignRole(u.id, role))}
                    onRemove={(role) => mutate(u.id, () => api.adminRemoveRole(u.id, role))}
                  />
                ))}
              </ul>
            )}
          </div>

          <nav className="flex items-center justify-between mt-4" aria-label="Pagination">
            <button
              onClick={() => goToPage(page - 1)}
              disabled={page === 1}
              className="px-3 py-1.5 text-sm font-medium text-primary hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
            >
              Previous
            </button>
            <span className="text-sm text-text-muted">Page {page}</span>
            {/* ponytail: API has no totalCount — Next is a full-page heuristic; an exact
                multiple of PAGE_SIZE can land on one empty page. Add totals if needed. */}
            <button
              onClick={() => goToPage(page + 1)}
              disabled={users.length < PAGE_SIZE}
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

function UserRow({
  u,
  roles,
  busy,
  onAssign,
  onRemove,
}: {
  u: AdminUser;
  roles: string[];
  busy: boolean;
  onAssign: (role: string) => void;
  onRemove: (role: string) => void;
}) {
  const [selected, setSelected] = useState("");
  const [confirming, setConfirming] = useState<string | null>(null);
  const assignable = roles.filter((r) => !u.roles.includes(r));

  return (
    <li className="py-4 grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
      <div className="min-w-0">
        <p className="font-medium text-text-heading truncate">{u.fullName}</p>
        <p className="text-sm text-text-muted truncate">{u.email ?? "no email"}</p>
        <div className="flex flex-wrap items-center gap-2 mt-2">
          {u.roles.length === 0 && <span className="text-xs text-text-muted">No roles</span>}
          {u.roles.map((role) =>
            confirming === role ? (
              <span
                key={role}
                className="inline-flex items-center gap-2 rounded-full bg-[#FEF2F2] border border-[#FECACA] px-2.5 py-1 text-xs font-medium text-[#B91C1C]"
              >
                Remove {role}?
                <button
                  onClick={() => {
                    setConfirming(null);
                    onRemove(role);
                  }}
                  disabled={busy}
                  className="font-semibold underline underline-offset-2 hover:no-underline cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Yes
                </button>
                <button
                  onClick={() => setConfirming(null)}
                  className="text-text-muted hover:text-text-heading cursor-pointer"
                >
                  Cancel
                </button>
              </span>
            ) : (
              <span
                key={role}
                className="inline-flex items-center gap-1 rounded-full bg-accent-light-blue text-primary px-2.5 py-1 text-xs font-medium"
              >
                {role}
                <button
                  onClick={() => setConfirming(role)}
                  disabled={busy}
                  aria-label={`Remove ${role} from ${u.fullName}`}
                  className="hover:text-primary-dark cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <XIcon />
                </button>
              </span>
            )
          )}
        </div>
      </div>

      <div className="flex items-center gap-2">
        {assignable.length > 0 && (
          <>
            <select
              aria-label={`Add role for ${u.fullName}`}
              value={selected}
              onChange={(e) => setSelected(e.target.value)}
              disabled={busy}
              className="rounded-xl border border-border bg-bg-card px-3 py-1.5 text-sm text-text-body outline-none focus:border-primary disabled:opacity-50"
            >
              <option value="">Add role&hellip;</option>
              {assignable.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
            <button
              onClick={() => {
                const role = selected;
                setSelected("");
                if (role) onAssign(role);
              }}
              disabled={busy || !selected}
              className="px-3 py-1.5 text-sm font-semibold text-white bg-primary hover:bg-primary-dark rounded-xl transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Assign
            </button>
          </>
        )}
      </div>
    </li>
  );
}

function XIcon() {
  return (
    <svg
      width="10"
      height="10"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="3"
      strokeLinecap="round"
      aria-hidden="true"
    >
      <path d="M18 6 6 18M6 6l12 12" />
    </svg>
  );
}

function UsersSkeleton() {
  return (
    <div className="card" aria-hidden="true">
      <div className="space-y-5">
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className="space-y-2">
            <div className="h-4 w-40 bg-border rounded animate-pulse" />
            <div className="h-3 w-64 bg-border rounded animate-pulse" />
          </div>
        ))}
      </div>
    </div>
  );
}
