"use client";

import { useAuth } from "@/lib/auth";
import { useRouter, usePathname } from "next/navigation";
import { useEffect } from "react";
import SkeletonLoader from "@/components/SkeletonLoader";

export default function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { user, loading, view } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  // Admin surfaces mounted under /admin also use this guard (tutor sessions),
  // so only the student routes bounce back when the admin view is active.
  const inAdminView = view === "admin" && !pathname.startsWith("/admin");

  useEffect(() => {
    if (!loading && !user) {
      router.replace("/login");
    } else if (!loading && inAdminView) {
      router.replace("/admin");
    }
  }, [user, loading, inAdminView, router]);

  if (loading) {
    return <SkeletonLoader />;
  }

  if (!user || inAdminView) {
    return null;
  }

  return <>{children}</>;
}
