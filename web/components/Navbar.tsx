"use client";

import Link from "next/link";
import { useState } from "react";
import { usePathname } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { hasAdminRole } from "@/lib/admin-nav";

const navLinks = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/practice", label: "Practice" },
  { href: "/interview", label: "Interview" },
  { href: "/jd", label: "JD" },
  { href: "/dashboard#tutor", label: "Tutor" },
];

export default function Navbar() {
  const { user, logout } = useAuth();
  const pathname = usePathname();
  // The desktop nav is hidden below sm and there was no mobile menu, so the
  // Interview link was unreachable at 390px without a JD link (CodeRabbit #176).
  const [menuOpen, setMenuOpen] = useState(false);
  // #102: every admin role gets the admin area; the shell lays out its sections.
  const links = hasAdminRole(user?.roles)
    ? [...navLinks, { href: "/admin", label: "Admin" }]
    : navLinks;

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

        <nav className="hidden md:flex items-center gap-1">
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
          <button
            type="button"
            onClick={() => setMenuOpen((open) => !open)}
            aria-label="Menu"
            aria-expanded={menuOpen}
            className="md:hidden p-2 -mr-1 text-text-muted hover:text-text-heading hover:bg-accent-light-blue rounded-xl transition-colors cursor-pointer"
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
        <nav className="md:hidden border-t border-border bg-white px-6 py-3 flex flex-col gap-1">
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
        </nav>
      )}
    </header>
  );
}
