"use client";

import Link from "next/link";
import { useAuth } from "@/lib/auth";
import { visibleNavItems } from "@/lib/admin-nav";

export default function AdminOverviewPage() {
  const { user } = useAuth();
  const items = visibleNavItems(user?.roles);

  return (
    <>
      <h1 className="text-2xl font-heading font-bold text-text-heading">Admin</h1>
      <p className="text-sm text-text-muted mt-1">Signed in as {user?.fullName}.</p>

      <div className="grid gap-4 sm:grid-cols-2 mt-6">
        {items.map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className="card block"
          >
            <h2 className="font-heading font-semibold text-text-heading">{item.label}</h2>
            <p className="text-sm text-text-muted mt-1">{item.description}</p>
          </Link>
        ))}
      </div>
    </>
  );
}
