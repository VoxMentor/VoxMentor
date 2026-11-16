"use client";

import { useAuth } from "@/lib/auth";
import ProtectedRoute from "@/components/ProtectedRoute";
import Navbar from "@/components/Navbar";

/**
 * Account settings. Read-only for now: the API has no profile-update endpoint,
 * so this shows what the session actually carries instead of fake inputs.
 */
export default function SettingsPage() {
  const { user, logout } = useAuth();

  const rows = [
    { label: "Full name", value: user?.fullName ?? "—" },
    { label: "Email", value: user?.email ?? "—" },
    { label: "Roles", value: user?.roles?.join(", ") || "—" },
  ];

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <main className="max-w-3xl mx-auto px-6 py-10">
          <div className="mb-8">
            <h1 className="font-heading text-2xl sm:text-3xl font-bold text-navy">
              Settings
            </h1>
            <p className="text-text-muted mt-2">
              Your profile and account preferences.
            </p>
          </div>

          <div className="card">
            <h2 className="font-heading font-semibold text-navy mb-4">
              Profile
            </h2>
            <dl className="divide-y divide-border">
              {rows.map((row) => (
                <div
                  key={row.label}
                  className="flex items-center justify-between gap-4 py-3"
                >
                  <dt className="text-sm text-text-muted">{row.label}</dt>
                  <dd className="text-sm font-medium text-navy text-right break-all">
                    {row.value}
                  </dd>
                </div>
              ))}
            </dl>
            <p className="text-xs text-text-muted mt-4">
              Name and email changes are not available in this build.
            </p>
          </div>

          <div className="card mt-5 flex items-center justify-between gap-4">
            <div>
              <h2 className="font-heading font-semibold text-navy">Session</h2>
              <p className="text-sm text-text-muted mt-1">
                Sign out on this device.
              </p>
            </div>
            <button
              onClick={() => void logout()}
              className="btn-secondary cursor-pointer"
            >
              Sign out
            </button>
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}
