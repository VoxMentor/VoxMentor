"use client";

import { useAuth } from "@/lib/auth";
import ProtectedRoute from "@/components/ProtectedRoute";
import Link from "next/link";

export default function DashboardPage() {
  const { user, logout } = useAuth();

  return (
    <ProtectedRoute>
      <div className="min-h-screen bg-bg-light">
        {/* Top Nav */}
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
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2">
                <div className="w-8 h-8 rounded-full bg-gradient-to-br from-primary to-primary-dark flex items-center justify-center text-white text-sm font-semibold">
                  {user?.fullName?.charAt(0) ?? "?"}
                </div>
                <span className="text-sm font-medium text-text-heading hidden sm:block">
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
        </header>

        {/* Main */}
        <main className="max-w-6xl mx-auto px-6 py-10">
          {/* Welcome banner */}
          <div className="relative overflow-hidden rounded-card-lg bg-gradient-to-br from-navy via-navy-light to-primary p-8 mb-8">
            <div className="absolute inset-0 opacity-20">
              <div className="absolute inset-0" style={{
                backgroundImage: 'radial-gradient(circle at 20% 50%, rgba(125,211,232,0.4) 0%, transparent 50%), radial-gradient(circle at 80% 20%, rgba(214,240,250,0.3) 0%, transparent 50%)'
              }} />
              <div className="absolute inset-0" style={{
                backgroundImage: 'radial-gradient(circle, rgba(255,255,255,0.06) 1px, transparent 1px)',
                backgroundSize: '24px 24px'
              }} />
            </div>
            <div className="relative z-10">
              <h1 className="text-2xl font-heading font-bold text-white mb-1">
                Welcome back, {user?.fullName?.split(" ")[0]}
              </h1>
              <p className="text-blue-200/70 text-sm">
                Ready to sharpen your interview skills? Pick up where you left off.
              </p>
            </div>
          </div>

          {/* Cards grid */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
            {[
              {
                icon: (
                  <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
                    <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                    <line x1="12" x2="12" y1="19" y2="22" />
                  </svg>
                ),
                title: "Practice Interview",
                desc: "Start a live mock interview with AI feedback.",
                iconBg: "bg-primary/10 text-primary",
              },
              {
                icon: (
                  <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z" />
                    <polyline points="14 2 14 8 20 8" />
                    <line x1="16" x2="8" y1="13" y2="13" />
                    <line x1="16" x2="8" y1="17" y2="17" />
                  </svg>
                ),
                title: "Review History",
                desc: "View feedback and scores from past sessions.",
                iconBg: "bg-accent-gold/10 text-accent-gold",
              },
              {
                icon: (
                  <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" />
                    <circle cx="12" cy="12" r="3" />
                  </svg>
                ),
                title: "Settings",
                desc: "Manage your profile and preferences.",
                iconBg: "bg-accent-teal/10 text-accent-teal",
              },
            ].map((card) => (
              <button
                key={card.title}
                className="card group text-left cursor-pointer"
              >
                <div className={`icon-circle-sm ${card.iconBg} mb-4`}>
                  {card.icon}
                </div>
                <h3 className="font-heading font-semibold text-navy mb-1 group-hover:text-primary transition-colors">
                  {card.title}
                </h3>
                <p className="text-sm text-text-muted leading-relaxed">
                  {card.desc}
                </p>
              </button>
            ))}
          </div>
        </main>
      </div>
    </ProtectedRoute>
  );
}
