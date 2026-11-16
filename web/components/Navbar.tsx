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
  // The admin view keeps the student links out of the navbar. The admin shell
  // already owns its own sidebar.
  const links = view === "admin" ? adminLinks : navLinks;

  // #102: "Admin" is a parent route (/admin, /admin/users, ...), so it must
  // prefix-match; the other links are leaves.
  const isActive = (link: { href: string }) =>
    link.href === "/admin"
      ? pathname === "/admin" || pathname.startsWith("/admin/")
      : pathname === link.href;

  const linkClass = (active: boolean) =>
    `inline-flex h-10 items-center border-b-2 px-1 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy ${
      active
        ? "border-navy text-navy"
        : "border-transparent text-text-muted hover:text-navy"
    }`;

  return (
    <header className="navbar">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4 sm:px-6">
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
              aria-current={isActive(link) ? "page" : undefined}
              className={linkClass(isActive(link))}
            >
              {link.label}
            </Link>
          ))}
        </nav>

        <div className="flex items-center gap-2 sm:gap-3">
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
            className="lg:hidden -mr-1 cursor-pointer rounded-[10px] p-2 text-text-muted transition-colors hover:bg-bg-light hover:text-navy focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy"
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
            <div className="flex h-8 w-8 items-center justify-center rounded-full bg-navy-light text-sm font-semibold text-white">
              {user?.fullName?.charAt(0) ?? "?"}
            </div>
            <span className="hidden text-sm font-medium text-navy md:block">
              {user?.fullName}
            </span>
          </div>
          <button
            type="button"
            onClick={logout}
            className="cursor-pointer whitespace-nowrap rounded-[10px] px-2 py-1.5 text-sm font-medium text-text-muted transition-colors hover:text-navy focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy"
          >
            Sign out
          </button>
        </div>
      </div>

      {/* Mobile menu: the only path to the routes the hidden desktop nav holds. */}
      {menuOpen && (
        <nav className="flex flex-col gap-1 border-t border-border bg-bg-card px-6 py-3 lg:hidden">
          {links.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              onClick={() => setMenuOpen(false)}
              aria-current={isActive(link) ? "page" : undefined}
              className={`block rounded-[10px] px-3 py-2 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy ${
                isActive(link)
                  ? "bg-accent-light-blue/60 text-navy"
                  : "text-text-muted hover:bg-bg-light hover:text-navy"
              }`}
            >
              {link.label}
            </Link>
          ))}
          {canSwitchView(user?.roles) && (
            <div
              className="mt-2 border-t border-border pt-3"
              onClick={() => setMenuOpen(false)}
            >
              <p className="px-1 pb-2 text-xs font-medium text-text-muted">
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
            className={`flex-1 cursor-pointer rounded-[8px] px-3 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy ${
              full ? "py-1.5" : "py-1"
            } ${
              active
                ? "bg-navy text-white"
                : "text-text-body hover:text-navy"
            }`}
          >
            {label}
          </button>
        );
      })}
    </div>
  );
}
