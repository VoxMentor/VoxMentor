"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import AdminRoute from "@/components/AdminRoute";
import Navbar from "@/components/Navbar";
import { useAuth } from "@/lib/auth";
import { visibleNavItems, type AdminNavItem } from "@/lib/admin-nav";

// Issue #102: role-gated admin shell — guard, sidebar navigation and main content area.
export default function AdminLayout({ children }: LayoutProps<"/admin">) {
  return (
    <AdminRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />
        <AdminShell>{children}</AdminShell>
      </div>
    </AdminRoute>
  );
}

function AdminShell({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuTriggerRef = useRef<HTMLButtonElement>(null);

  // Escape closes the modal drawer; return focus to the trigger (APG dialog pattern).
  useEffect(() => {
    if (!menuOpen) return;
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        setMenuOpen(false);
        menuTriggerRef.current?.focus();
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [menuOpen]);

  // Close mobile drawer on route change (Back/Forward or desktop sidebar nav).
  // React's documented "adjust state when a prop changes" pattern — setState
  // during render, guarded, instead of an effect (avoids cascading renders).
  const [prevPathname, setPrevPathname] = useState(pathname);
  if (prevPathname !== pathname) {
    setPrevPathname(pathname);
    setMenuOpen(false);
  }


  const items = visibleNavItems(user?.roles);

  return (
    <div className="flex w-full max-w-7xl mx-auto px-6">
      <aside className="hidden lg:block w-56 shrink-0 pt-10">
        <nav aria-label="Admin sections">
          <NavLinks items={items} pathname={pathname} />
        </nav>
      </aside>

      <main className="flex-1 min-w-0 py-10">
        {/* Sidebar collapses below lg: a menu button takes over */}
        <div className="lg:hidden pb-6">
          <button
            ref={menuTriggerRef}
            onClick={() => setMenuOpen(true)}
            aria-expanded={menuOpen}
            aria-controls="admin-mobile-nav"
            className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium text-text-muted hover:text-text-heading hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer"
          >
            <MenuIcon />
            Admin sections
          </button>
        </div>
        {children}
      </main>

      {menuOpen && (
        <div
          id="admin-mobile-nav"
          role="dialog"
          aria-modal="true"
          aria-label="Admin sections"
          className="fixed inset-0 z-40 lg:hidden"
        >
          <div
            className="absolute inset-0 bg-navy-deep/40"
            onClick={() => setMenuOpen(false)}
            aria-hidden="true"
          />
          <div
            className="absolute inset-y-0 left-0 w-72 max-w-[85vw] bg-bg-card p-6 overflow-y-auto shadow-lg"
            // Focus trap: Tab wraps inside the modal drawer instead of reaching
            // the obscured background controls (aria-modal claims exclusivity).
            onKeyDown={(e) => {
              if (e.key !== "Tab") return;
              const focusables = e.currentTarget.querySelectorAll<HTMLElement>(
                "a[href], button:not([disabled])"
              );
              const first = focusables[0];
              const last = focusables[focusables.length - 1];
              if (!first) return;
              if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last?.focus();
              } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
              }
            }}
          >
            <div className="flex items-center justify-between mb-6">
              <span className="font-heading font-bold text-text-heading">Admin</span>
              <button
                onClick={() => setMenuOpen(false)}
                autoFocus
                aria-label="Close admin sections menu"
                className="p-2 -mr-2 text-text-muted hover:text-text-heading rounded-xl hover:bg-accent-light-blue transition-colors cursor-pointer"
              >
                <XIcon />
              </button>
            </div>
            <NavLinks items={items} pathname={pathname} onNavigate={() => setMenuOpen(false)} />
          </div>
        </div>
      )}
    </div>
  );
}

function NavLinks({
  items,
  pathname,
  onNavigate,
}: {
  items: AdminNavItem[];
  pathname: string;
  onNavigate?: () => void;
}) {
  return (
    <ul className="flex flex-col gap-1">
      {items.map((item) => {
        // Overview (href="/admin") is a parent route — prefix-match like Navbar;
        // leaf items (questions, corpus, etc.) use exact match.
        const active = item.href === "/admin"
          ? pathname === "/admin" || pathname.startsWith("/admin/")
          : pathname === item.href;
        return (
          <li key={item.href}>
            <Link
              href={item.href}
              onClick={onNavigate}
              aria-current={active ? "page" : undefined}
              className={`block px-3 py-2 text-sm font-medium rounded-xl transition-colors ${
                active
                  ? "text-primary bg-accent-light-blue"
                  : "text-text-muted hover:text-text-heading hover:bg-accent-light-blue"
              }`}
            >
              {item.label}
            </Link>
          </li>
        );
      })}
    </ul>
  );
}

function MenuIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true">
      <path d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  );
}

function XIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true">
      <path d="M18 6 6 18M6 6l12 12" />
    </svg>
  );
}
