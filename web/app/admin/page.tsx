"use client";

import Link from "next/link";
import { useAuth } from "@/lib/auth";
import { visibleNavItems } from "@/lib/admin-nav";

export default function AdminOverviewPage() {
  const { user } = useAuth();
  const items = visibleNavItems(user?.roles);

  return (
    <>
      <header className="mb-8">
        <h1 className="font-heading text-2xl font-semibold text-navy">
          Admin console
        </h1>
        <p className="mt-1 max-w-2xl text-sm text-text-muted">
          Sections available to {user?.fullName ?? "your account"}.
        </p>
      </header>

      <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,1fr)_280px]">
        <section className="panel">
          <div className="flex items-baseline justify-between gap-3">
            <h2 className="text-sm font-medium text-navy">Section index</h2>
            <span className="font-mono text-xs text-text-muted">
              {items.length} sections
            </span>
          </div>

          <ul className="mt-4 flex flex-col">
            {items.map((item) => (
              <li key={item.href}>
                <Link
                  href={item.href}
                  className="group block border-t border-border py-4 first:border-t-0 first:pt-0 last:pb-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy"
                >
                  <span className="block text-sm font-medium text-navy transition-colors group-hover:text-primary-dark">
                    {item.label}
                  </span>
                  <span className="mt-1 block text-xs leading-relaxed text-text-muted">
                    {item.description}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </section>

        <aside className="panel">
          <h2 className="text-sm font-medium text-navy">Access</h2>
          <p className="mt-3 text-xs text-text-muted">Signed in as</p>
          <p className="mt-1 break-words text-sm text-navy">
            {user?.fullName ?? "n/a"}
          </p>
          <p className="mt-4 text-xs text-text-muted">Roles</p>
          {user?.roles?.length ? (
            <ul className="mt-2 flex flex-wrap gap-2">
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
            <p className="mt-2 text-sm text-text-muted">No roles assigned</p>
          )}
          <p className="mt-5 border-t border-border pt-4 text-xs leading-relaxed text-text-muted">
            The index shows only sections your roles can access
          </p>
        </aside>
      </div>
    </>
  );
}
