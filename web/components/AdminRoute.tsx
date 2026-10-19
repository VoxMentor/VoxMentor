"use client";

import { useAuth } from "@/lib/auth";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import SkeletonLoader from "@/components/SkeletonLoader";
import { hasAdminRole } from "@/lib/admin-nav";

// Issue #102: gate for the /admin area.
// Anonymous -> /login is handled by AuthProvider; non-admin -> /dashboard here.
// UX-only guard: every admin endpoint enforces RBAC server-side.
export default function AdminRoute({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth();
  const router = useRouter();

  // Anonymous redirect is AuthProvider's job (it owns the public-pages effect);
  // duplicating it here caused a double router.replace. This effect only handles
  // the signed-in-but-not-admin case, which AuthProvider knows nothing about.
  // Guard layering (Issue #102): shell = any admin role; pages enforce their own
  // stricter policy (e.g. /admin/users = SuperAdmin-only, mirrors ManageRoles).
  useEffect(() => {
    if (loading || !user) return;
    if (!hasAdminRole(user.roles)) {
      router.replace("/dashboard");
    }
  }, [user, loading, router]);

  if (loading) {
    return <SkeletonLoader />;
  }

  if (!user || !hasAdminRole(user.roles)) {
    return null;
  }

  return <>{children}</>;
}
