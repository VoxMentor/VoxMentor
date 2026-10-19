// Role gates for the admin shell (Issue #102).
// Mirrors backend RBAC policies in Program.cs (ManageContent / ManagePlatform / ManageRoles) — keep in sync.

export const ADMIN_ROLES = ["SuperAdmin", "ContentAdmin", "PlatformAdmin"] as const;

export function hasAdminRole(roles?: string[]): boolean {
  return !!roles?.some((role) => (ADMIN_ROLES as readonly string[]).includes(role));
}

const CONTENT_ADMINS = ["SuperAdmin", "ContentAdmin"];
const PLATFORM_ADMINS = ["SuperAdmin", "PlatformAdmin"];

export interface AdminNavItem {
  href: string;
  label: string;
  description: string;
  roles: string[];
  issue?: number;
}

export const ADMIN_NAV: AdminNavItem[] = [
  {
    href: "/admin",
    label: "Overview",
    description: "Summary of the sections your role can access.",
    roles: [...ADMIN_ROLES],
  },
  {
    href: "/admin/questions",
    label: "Question bank",
    description: "Manage practice questions: list, create and edit them.",
    roles: CONTENT_ADMINS,
    issue: 103,
  },
  {
    href: "/admin/corpus",
    label: "Corpus upload",
    description: "Upload textbook PDFs and track ingestion job status.",
    roles: CONTENT_ADMINS,
    issue: 104,
  },
  {
    href: "/admin/audit",
    label: "Audit log",
    description: "A chronological record of administrative actions.",
    roles: PLATFORM_ADMINS,
    issue: 106,
  },
  {
    href: "/admin/plagiarism",
    label: "Plagiarism review",
    description: "Review flagged submissions and resolve reports.",
    roles: PLATFORM_ADMINS,
    issue: 107,
  },
  {
    href: "/admin/tutor-sessions",
    label: "Tutor sessions",
    description: "View tutor sessions across the platform.",
    roles: PLATFORM_ADMINS,
    issue: 108,
  },
  {
    href: "/admin/users",
    label: "Users & roles",
    description: "List users and grant or revoke admin roles.",
    roles: ["SuperAdmin"],
  },
];

// Single source for "which admin sections does this user see" — used by the
// sidebar and the /admin overview so the two can never drift.
export function visibleNavItems(userRoles?: string[]): AdminNavItem[] {
  return ADMIN_NAV.filter((item) => item.roles.some((role) => userRoles?.includes(role)));
}
