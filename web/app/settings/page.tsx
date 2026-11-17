"use client";

import { useAuth } from "@/lib/auth";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";

/**
 * Account settings. Read-only: the API has no profile-update endpoint, so this
 * shows what the session actually carries rather than presenting fake inputs.
 */
export default function SettingsPage() {
  const { user, logout } = useAuth();

  const rows = [
    { label: "Full name", value: user?.fullName ?? "n/a" },
    { label: "Email", value: user?.email ?? "n/a" },
    { label: "Account id", value: user?.id ?? "n/a", mono: true },
  ];
  const initial = user?.fullName?.charAt(0) ?? "?";

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="mx-auto w-full max-w-[1400px] px-8 py-10">
          <header className="mb-8">
            <h1 className="font-heading text-2xl font-semibold text-navy">
              Settings
            </h1>
            <p className="mt-1 text-sm text-text-muted">
              Your account, roles, and active session.
            </p>
          </header>

          <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,1fr)_320px]">
            <section className="panel">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="flex items-center gap-3">
                  <div className="flex h-10 w-10 items-center justify-center rounded-full bg-navy-light text-sm font-medium text-white">
                    {initial}
                  </div>
                  <div className="min-w-0">
                    <p className="text-xs text-text-muted">Signed in as</p>
                    <p className="break-words font-heading text-xl font-semibold text-navy">
                      {user?.fullName ?? "Account"}
                    </p>
                  </div>
                </div>
                {user?.id && (
                  <span className="font-mono text-xs text-text-muted">
                    ID {user.id.slice(0, 8)}
                  </span>
                )}
              </div>

              <div className="mt-5 border-t border-border pt-4">
                <h2 className="text-sm font-medium text-navy">Account details</h2>
                <dl className="mt-3 grid grid-cols-1 gap-x-6 gap-y-3 sm:grid-cols-[160px_minmax(0,1fr)]">
                  {rows.map((row) => (
                    <div key={row.label} className="contents">
                      <dt className="text-xs text-text-muted">{row.label}</dt>
                      <dd
                        className={`break-all text-sm text-navy ${
                          row.mono ? "font-mono text-xs" : ""
                        }`}
                      >
                        {row.value}
                      </dd>
                    </div>
                  ))}
                </dl>
              </div>

              <p className="mt-5 text-xs text-text-muted">
                Name and email changes are not available in this build.
              </p>
            </section>

            <aside className="flex min-w-0 flex-col gap-5">
              <section className="panel">
                <h2 className="text-sm font-medium text-navy">Session</h2>
                <p className="mt-3 flex items-center gap-2 text-sm text-text-muted">
                  <span
                    aria-hidden="true"
                    className="h-1.5 w-1.5 rounded-full bg-success"
                  />
                  Signed in
                </p>
                <p className="mt-1 text-xs text-text-muted">
                  Sign out on this device.
                </p>
                <button
                  onClick={() => void logout()}
                  className="btn-solid mt-4 w-full"
                >
                  Sign out
                </button>
              </section>

              <section className="panel">
                <h2 className="text-sm font-medium text-navy">Roles</h2>
                {user?.roles?.length ? (
                  <ul className="mt-3 flex flex-wrap gap-2">
                    {user.roles.map((role) => (
                      <li
                        key={role}
                        className="rounded-[6px] border border-border px-2 py-1 font-mono text-xs text-navy"
                      >
                        {role}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="mt-2 text-sm text-text-muted">No roles assigned.</p>
                )}
              </section>
            </aside>
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}
