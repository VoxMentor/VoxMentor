// Frontend-only "view" switch for dual-role accounts (student + admin).
// The API and RBAC are unchanged: this only decides which surface the signed-in
// user is looking at, so an admin who is also a student stops tripping over
// student widgets that 403 for their role.
import { hasAdminRole } from "./admin-nav.ts";

export type ViewMode = "student" | "admin";

const STORAGE_PREFIX = "voxmentor:view:";

// Admin-only accounts land in admin view; everyone else starts as a student.
export function defaultView(roles?: string[]): ViewMode {
  return hasAdminRole(roles) && !roles?.includes("Student") ? "admin" : "student";
}

// The switch is only offered when both surfaces would actually work.
export function canSwitchView(roles?: string[]): boolean {
  return hasAdminRole(roles) && !!roles?.includes("Student");
}

export function landingPath(view: ViewMode): string {
  return view === "admin" ? "/admin" : "/dashboard";
}

// A saved preference only counts while the account still has a choice.
export function resolveView(saved: ViewMode | null, roles?: string[]): ViewMode {
  return canSwitchView(roles) ? saved ?? defaultView(roles) : defaultView(roles);
}

function storage(): Storage | null {
  try {
    return typeof window === "undefined" ? null : window.localStorage;
  } catch {
    return null;
  }
}

export function loadView(userId?: string | null): ViewMode | null {
  if (!userId) return null;
  try {
    const value = storage()?.getItem(STORAGE_PREFIX + userId);
    return value === "admin" || value === "student" ? value : null;
  } catch {
    return null;
  }
}

export function saveView(
  userId: string | null | undefined,
  view: ViewMode | null
): void {
  if (!userId) return;
  try {
    if (view) storage()?.setItem(STORAGE_PREFIX + userId, view);
    else storage()?.removeItem(STORAGE_PREFIX + userId);
  } catch {
    // Storage unavailable (private mode, quota): the view just keeps its default.
  }
}
