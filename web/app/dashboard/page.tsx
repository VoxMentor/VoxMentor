"use client";

import Link from "next/link";
import ProtectedRoute from "@/components/ProtectedRoute";
import TutorChat from "@/components/TutorChat";
import Navbar from "@/components/Navbar";
import MasteryHeatmap from "@/components/MasteryHeatmap";
import ReadinessOverview from "@/components/ReadinessOverview";
import RecentActivity from "@/components/RecentActivity";

export default function DashboardPage() {
  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        <Navbar />

        <main className="mx-auto max-w-6xl px-6 py-10">
          <header className="mb-8 flex flex-wrap items-end justify-between gap-4">
            <div>
              <h1 className="font-heading text-2xl font-semibold text-navy">
                Dashboard
              </h1>
              <p className="mt-1 text-sm text-text-muted">
                Concept mastery, interview readiness, and recent practice.
              </p>
            </div>
            <div className="flex items-center gap-4">
              <Link
                href="/settings"
                className="text-sm font-medium text-text-muted transition-colors hover:text-navy"
              >
                Settings
              </Link>
              <Link href="/practice" className="btn-solid">
                Start practice
              </Link>
            </div>
          </header>

          <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-3">
            <div className="lg:col-span-2">
              <MasteryHeatmap />
            </div>
            <div className="flex flex-col gap-5">
              <ReadinessOverview />
              <RecentActivity />
            </div>
          </div>

          <div className="mt-5" id="tutor">
            <TutorChat />
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}
