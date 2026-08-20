"use client";

import { useAuth } from "@/lib/auth";
import SkeletonLoader from "@/components/SkeletonLoader";

export default function HomePage() {
  const { loading } = useAuth();

  if (loading) {
    return <SkeletonLoader />;
  }

  return null;
}
