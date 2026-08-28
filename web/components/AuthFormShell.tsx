"use client";

export default function AuthFormShell({
  heading,
  subheading,
  footer,
  children,
}: {
  heading: string;
  subheading: string;
  footer: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="auth-layout">
      {/* Brand Panel */}
      <div className="auth-brand">
        <div className="auth-grid-dots" />
        <div className="relative z-10 mb-auto pt-2">
          <div className="flex items-center gap-3 mb-2">
            <div className="logo-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
                <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                <line x1="12" x2="12" y1="19" y2="22" />
              </svg>
            </div>
            <span className="logo-text-white">VoxMentor</span>
          </div>
        </div>
        <div className="relative z-10">
          <h2 className="text-white text-3xl font-heading font-bold leading-tight tracking-tight mb-3">
            Ace your next<br />interview with AI.
          </h2>
          <p className="text-blue-200/70 text-base leading-relaxed max-w-sm">
            Practice with realistic mock interviews, get instant feedback, and build confidence before the real thing.
          </p>
          <div className="flex gap-6 mt-8">
            <div className="text-center">
              <div className="text-2xl font-heading font-bold text-white">10k+</div>
              <div className="text-xs text-blue-200/60 mt-0.5">Practice Sessions</div>
            </div>
            <div className="text-center">
              <div className="text-2xl font-heading font-bold text-white">94%</div>
              <div className="text-xs text-blue-200/60 mt-0.5">Success Rate</div>
            </div>
            <div className="text-center">
              <div className="text-2xl font-heading font-bold text-white">50+</div>
              <div className="text-xs text-blue-200/60 mt-0.5">Job Roles</div>
            </div>
          </div>
        </div>
      </div>

      {/* Form Panel */}
      <div className="auth-form-panel">
        <div className="glass-card">
          <div className="mb-8">
            <h1 className="text-2xl font-heading font-bold text-navy tracking-tight mb-1">
              {heading}
            </h1>
            <p className="text-sm text-text-muted">{subheading}</p>
          </div>

          {children}

          <div className="mt-6 text-center text-sm text-text-muted">
            {footer}
          </div>
        </div>
      </div>
    </div>
  );
}
