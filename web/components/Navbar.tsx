"use client";

import Link from "next/link";
import { useState } from "react";
import { usePathname } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { canSwitchView, type ViewMode } from "@/lib/view";

const navLinks = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/practice", label: "Practice" },
  { href: "/interview", label: "Interview" },
  { href: "/jd", label: "JD" },
  { href: "/dashboard#tutor", label: "Tutor" },
];

const adminLinks = [{ href: "/admin", label: "Admin" }];

const VIEW_OPTIONS: { value: ViewMode; label: string }[] = [
  { value: "student", label: "Student" },
  { value: "admin", label: "Admin" },
];

export default function Navbar() {
  const { user, view, logout } = useAuth();
  const pathname = usePathname();
  // The desktop nav is hidden below lg and the mobile menu carries every link,
  // so the Interview link stays reachable at 390px (CodeRabbit #176).
  const [menuOpen, setMenuOpen] = useState(false);
  // #102: every admin role gets the admin area; the shell lays out its sections.
  // The admin view keeps the student links out of the navbar — the admin shell
  // already owns its own sidebar.
  const links = view === "admin" ? adminLinks : navLinks;

  // #102: "Admin" is a parent route (/admin, /admin/users, ...), so it must
  // prefix-match; the other links are leaves.
  const isActive = (link: { href: string }) =>
    link.href === "/admin"
      ? pathname === "/admin" || pathname.startsWith("/admin/")
      : pathname === link.href;

  const linkClass = (active: boolean) =>
    `px-3 py-1.5 text-sm font-medium rounded-xl transition-colors ${
      active
        ? "text-primary bg-accent-light-blue"
        : "text-text-muted hover:text-text-heading hover:bg-accent-light-blue"
    }`;

  return (
    <header className="navbar scrolled">
      <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between">
        <Link href="/" className="flex items-center gap-3">
          <div className="logo-icon !w-9 !h-9 !rounded-lg">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
              <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
              <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
              <line x1="12" x2="12" y1="19" y2="22" />
            </svg>
          </div>
          <span className="logo-text">VoxMentor</span>
        </Link>

        <nav className="hidden lg:flex items-center gap-1">
          {links.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className={linkClass(isActive(link))}
            >
              {link.label}
            </Link>
          ))}
        </nav>

        <div className="flex items-center gap-4">
          {canSwitchView(user?.roles) && (
            <div className="hidden lg:block">
              <ViewSwitch />
            </div>
          )}
          <button
            type="button"
            onClick={() => setMenuOpen((open) => !open)}
            aria-label="Menu"
            aria-expanded={menuOpen}
            className="lg:hidden p-2 -mr-1 text-text-muted hover:text-text-heading hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
              strokeLinecap="round"
              aria-hidden="true"
            >
              {menuOpen ? (
                <>
                  <line x1="6" y1="6" x2="18" y2="18" />
                  <line x1="6" y1="18" x2="18" y2="6" />
                </>
              ) : (
                <>
                  <line x1="4" y1="7" x2="20" y2="7" />
                  <line x1="4" y1="12" x2="20" y2="12" />
                  <line x1="4" y1="17" x2="20" y2="17" />
                </>
              )}
            </svg>
          </button>
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-full bg-gradient-to-br from-primary to-primary-dark flex items-center justify-center text-white text-sm font-semibold">
              {user?.fullName?.charAt(0) ?? "?"}
            </div>
            <span className="text-sm font-medium text-text-heading hidden md:block">
              {user?.fullName}
            </span>
          </div>
          <button
            onClick={logout}
            className="px-3 py-1.5 text-sm font-medium text-text-muted hover:text-text-heading hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer"
          >
            Sign out
          </button>
        </div>
      </div>

      {/* Mobile menu: the only path to the routes the hidden desktop nav holds. */}
      {menuOpen && (
        <nav className="lg:hidden border-t border-border bg-white px-6 py-3 flex flex-col gap-1">
          {links.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              onClick={() => setMenuOpen(false)}
              className={linkClass(isActive(link))}
            >
              {link.label}
            </Link>
          ))}
          {canSwitchView(user?.roles) && (
            <div
              className="mt-2 pt-3 border-t border-border"
              onClick={() => setMenuOpen(false)}
            >
              <p className="px-1 pb-2 text-xs font-medium text-text-body">
                Viewing as
              </p>
              <ViewSwitch full />
            </div>
          )}
        </nav>
      )}
    </header>
  );
}

// Compact segmented control: the selected segment is the surface in use, so
// there is no "what does this button do" guesswork.
function ViewSwitch({ full = false }: { full?: boolean }) {
  const { view, switchView } = useAuth();

  return (
    <div
      role="group"
      aria-label="View mode"
      className={`flex items-center gap-0.5 rounded-[10px] border border-border bg-bg-light p-0.5 ${
        full ? "w-full" : ""
      }`}
    >
      {VIEW_OPTIONS.map(({ value, label }) => {
        const active = view === value;
        return (
          <button
            key={value}
            type="button"
            onClick={() => switchView(value)}
            aria-pressed={active}
            className={`flex-1 px-3 rounded-lg text-sm font-medium transition-colors cursor-pointer ${
              full ? "py-1.5" : "py-1"
            } ${
              active
                ? "bg-navy text-white shadow-sm"
                : "text-text-body hover:text-text-heading"
            }`}
          >
            {label}
          </button>
        );
      })}
    </div>
  );
}
