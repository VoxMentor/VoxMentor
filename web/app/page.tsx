"use client";

import { useAuth } from "@/lib/auth";
import Link from "next/link";
import { useEffect, useState } from "react";

const features = [
  {
    icon: (
      <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
        <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
        <line x1="12" x2="12" y1="19" y2="22" />
      </svg>
    ),
    title: "Mock Interviews",
    desc: "Practice with realistic AI-powered interview simulations tailored to your role.",
  },
  {
    icon: (
      <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z" />
        <polyline points="14 2 14 8 20 8" />
        <line x1="16" x2="8" y1="13" y2="13" />
        <line x1="16" x2="8" y1="17" y2="17" />
      </svg>
    ),
    title: "Instant Feedback",
    desc: "Get detailed analysis on your answers, tone, and confidence in real time.",
  },
  {
    icon: (
      <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" />
        <circle cx="12" cy="12" r="3" />
      </svg>
    ),
    title: "Progress Tracking",
    desc: "Monitor your improvement over time with detailed performance analytics.",
  },
  {
    icon: (
      <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="12" r="10" />
        <polyline points="12 6 12 12 16 14" />
      </svg>
    ),
    title: "50+ Job Roles",
    desc: "Interview prep for software engineering, product management, design, and more.",
  },
  {
    icon: (
      <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
        <path d="M16 3.13a4 4 0 0 1 0 7.75" />
      </svg>
    ),
    title: "Community",
    desc: "Join thousands of job seekers sharing tips and celebrating offers together.",
  },
  {
    icon: (
      <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 20h9" />
        <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z" />
      </svg>
    ),
    title: "Answer Builder",
    desc: "Craft compelling STAR-method responses with AI-guided suggestions.",
  },
];

const testimonials = [
  {
    name: "Alex Chen",
    role: "Software Engineer at Google",
    text: "VoxMentor completely transformed how I prepared for technical interviews. The AI feedback was spot-on and helped me land my dream job.",
    rating: 5,
  },
  {
    name: "Sarah Kim",
    role: "Product Manager at Meta",
    text: "The mock interviews felt incredibly real. I went in confident and got the offer. Best investment in my career so far.",
    rating: 5,
  },
  {
    name: "James Rivera",
    role: "UX Designer at Airbnb",
    text: "I practiced 30+ sessions before my interviews. The detailed feedback on my communication style was a game-changer.",
    rating: 5,
  },
];

const stats = [
  { value: "10,000+", label: "Practice Sessions" },
  { value: "94%", label: "Success Rate" },
  { value: "50+", label: "Job Roles" },
  { value: "4.9/5", label: "User Rating" },
];

const steps = [
  {
    num: "01",
    title: "Choose Your Role",
    desc: "Select from 50+ job categories to get tailored interview questions.",
  },
  {
    num: "02",
    title: "Practice with AI",
    desc: "Run mock interviews with our advanced AI that adapts to your answers.",
  },
  {
    num: "03",
    title: "Get Feedback",
    desc: "Receive instant, detailed analysis on every aspect of your performance.",
  },
  {
    num: "04",
    title: "Land the Job",
    desc: "Walk into your interview confident and prepared. Get the offer you deserve.",
  },
];

export default function HomePage() {
  const { user } = useAuth();
  const [scrolled, setScrolled] = useState(false);
  const primaryCtaHref = user ? "/dashboard" : "/register";
  const primaryCtaText = user ? "Go to Dashboard" : "Start Practicing Free";

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 10);
    window.addEventListener("scroll", onScroll);
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  return (
    <div className="min-h-screen bg-white">
      {/* ===== NAVBAR ===== */}
      <nav className={`navbar ${scrolled ? "scrolled" : ""}`}>
        <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between">
          <Link href="/" className="flex items-center gap-3">
            <div className="logo-icon !w-9 !h-9 !rounded-lg">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
                <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                <line x1="12" x2="12" y1="19" y2="22" />
              </svg>
            </div>
            <span className="logo-text">VoxMentor</span>
          </Link>

          <div className="hidden md:flex items-center gap-8">
            <a href="#features" className="nav-link">Features</a>
            <a href="#how-it-works" className="nav-link">How It Works</a>
            <a href="#testimonials" className="nav-link">Testimonials</a>
            <a href="#pricing" className="nav-link">Pricing</a>
          </div>

          <div className="flex items-center gap-3">
            {user ? (
              <Link href="/dashboard" className="btn-primary !py-2.5 !px-6 !text-sm">
                Dashboard
              </Link>
            ) : (
              <>
                <Link href="/login" className="btn-ghost">
                  Sign In
                </Link>
                <Link href="/register" className="btn-primary !py-2.5 !px-6 !text-sm">
                  Get Started Free
                </Link>
              </>
            )}
          </div>
        </div>
      </nav>

      {/* ===== HERO ===== */}
      <section className="hero-section">
        <div className="hero-blob hero-blob-1" />
        <div className="hero-blob hero-blob-2" />

        <div className="max-w-6xl mx-auto px-6 relative z-10">
          <div className="grid lg:grid-cols-2 gap-12 items-center">
            {/* Left - Copy */}
            <div>
              <div className="inline-flex items-center gap-2 px-4 py-2 rounded-full bg-white/80 border border-border text-sm font-medium text-text-body mb-6 shadow-soft">
                <span className="w-2 h-2 rounded-full bg-success animate-pulse" />
                AI-Powered Interview Prep
              </div>

              <h1 className="text-4xl md:text-5xl lg:text-[56px] font-heading font-bold text-navy leading-[1.1] tracking-tight mb-6">
                Ace your next
                <br />
                interview with
                <br />
                <span className="text-primary">confidence.</span>
              </h1>

              <p className="text-lg text-text-body leading-relaxed max-w-lg mb-8">
                Practice with realistic mock interviews, get instant AI feedback,
                and build the confidence you need to land your dream job.
              </p>

              <div className="flex flex-wrap items-center gap-4 mb-10">
                <Link href={primaryCtaHref} className="btn-primary">
                  {primaryCtaText}
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M5 12h14" />
                    <path d="m12 5 7 7-7 7" />
                  </svg>
                </Link>
                <a href="#how-it-works" className="btn-secondary">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <circle cx="12" cy="12" r="10" />
                    <polygon points="10 8 16 12 10 16 10 8" />
                  </svg>
                  See How It Works
                </a>
              </div>

              {/* Social proof */}
              <div className="flex items-center gap-4">
                <div className="flex -space-x-2">
                  {[...Array(4)].map((_, i) => (
                    <div
                      key={i}
                      className="w-9 h-9 rounded-full border-2 border-white bg-gradient-to-br from-primary to-primary-dark flex items-center justify-center text-white text-xs font-semibold"
                    >
                      {["A", "S", "J", "M"][i]}
                    </div>
                  ))}
                </div>
                <div>
                  <div className="flex items-center gap-1 text-accent-gold text-sm">
                    {[...Array(5)].map((_, i) => (
                      <svg key={i} width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
                        <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
                      </svg>
                    ))}
                  </div>
                  <p className="text-sm text-text-muted">
                    Trusted by <span className="font-semibold text-text-heading">10,000+</span> job seekers
                  </p>
                </div>
              </div>
            </div>

            {/* Right - Visual */}
            <div className="relative hidden lg:block">
              <div className="relative bg-white rounded-card-lg shadow-card p-6 border border-border">
                {/* Mock interview UI */}
                <div className="flex items-center gap-3 mb-4">
                  <div className="w-3 h-3 rounded-full bg-danger/80" />
                  <div className="w-3 h-3 rounded-full bg-accent-gold/80" />
                  <div className="w-3 h-3 rounded-full bg-success/80" />
                  <span className="ml-2 text-xs text-text-muted font-medium">Mock Interview — Software Engineer</span>
                </div>
                <div className="space-y-4">
                  <div className="bg-bg-light rounded-xl p-4">
                    <p className="text-sm font-medium text-navy mb-1">Interviewer</p>
                    <p className="text-sm text-text-body">Tell me about a time you handled a challenging bug in production.</p>
                  </div>
                  <div className="bg-primary/5 rounded-xl p-4 border border-primary/10">
                    <p className="text-sm font-medium text-primary mb-1">Your Answer</p>
                    <p className="text-sm text-text-body">In my previous role, I noticed a critical issue affecting 15% of users. I quickly set up a war room, identified the root cause using our monitoring tools...</p>
                  </div>
                  <div className="bg-success/5 rounded-xl p-4 border border-success/10">
                    <div className="flex items-center gap-2 mb-1">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#10B981" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                        <polyline points="20 6 9 17 4 12" />
                      </svg>
                      <p className="text-sm font-medium text-success">AI Feedback</p>
                    </div>
                    <p className="text-sm text-text-body">Strong STAR response. Great leadership signal. Consider quantifying the impact with specific metrics.</p>
                  </div>
                </div>
                {/* Score badge */}
                <div className="absolute -top-4 -right-4 bg-white rounded-card shadow-float p-3 flex items-center gap-2 border border-border">
                  <div className="w-10 h-10 rounded-full bg-success/10 flex items-center justify-center">
                    <span className="text-lg font-bold text-success">92</span>
                  </div>
                  <div>
                    <p className="text-xs font-semibold text-text-heading">Score</p>
                    <p className="text-xs text-text-muted">Excellent</p>
                  </div>
                </div>
              </div>

              {/* Floating stat cards */}
              <div className="absolute -bottom-6 -left-6 bg-white rounded-card shadow-float p-4 border border-border animate-slide-up animate-delay-3">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#4AADDB" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <polyline points="23 6 13.5 15.5 8.5 10.5 1 18" />
                      <polyline points="17 6 23 6 23 12" />
                    </svg>
                  </div>
                  <div>
                    <p className="text-xs text-text-muted">Sessions</p>
                    <p className="text-lg font-bold text-navy">147</p>
                  </div>
                </div>
              </div>

              <div className="absolute -top-2 left-1/2 -translate-x-1/2 bg-white rounded-card shadow-float px-4 py-2 border border-border animate-slide-up animate-delay-4">
                <p className="text-xs text-text-muted">Acceptance Rate</p>
                <p className="text-base font-bold text-success">+32%</p>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ===== STATS BAR ===== */}
      <section className="trusted-section py-10">
        <div className="max-w-6xl mx-auto px-6">
          <div className="grid grid-cols-2 md:grid-cols-4 gap-8">
            {stats.map((stat) => (
              <div key={stat.label} className="text-center">
                <p className="text-3xl md:text-4xl font-heading font-bold text-navy mb-1">{stat.value}</p>
                <p className="text-sm text-text-muted">{stat.label}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ===== FEATURES ===== */}
      <section id="features" className="py-20 md:py-28">
        <div className="max-w-6xl mx-auto px-6">
          <div className="text-center max-w-2xl mx-auto mb-16">
            <p className="text-sm font-semibold text-primary mb-3 tracking-wide uppercase">Features</p>
            <h2 className="text-3xl md:text-4xl font-heading font-bold text-navy mb-4">
              Everything you need to
              <br />
              land your dream job
            </h2>
            <p className="text-text-body text-lg">
              Our AI-powered platform gives you the tools, feedback, and confidence
              to ace any interview.
            </p>
          </div>

          <div className="grid md:grid-cols-2 lg:grid-cols-3 gap-6">
            {features.map((f) => (
              <div key={f.title} className="card group cursor-default">
                <div className="icon-circle mb-5 group-hover:scale-110 transition-transform duration-300">
                  {f.icon}
                </div>
                <h3 className="text-lg font-heading font-semibold text-navy mb-2">{f.title}</h3>
                <p className="text-sm text-text-body leading-relaxed">{f.desc}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ===== HOW IT WORKS ===== */}
      <section id="how-it-works" className="py-20 md:py-28 bg-bg-light">
        <div className="max-w-6xl mx-auto px-6">
          <div className="text-center max-w-2xl mx-auto mb-16">
            <p className="text-sm font-semibold text-primary mb-3 tracking-wide uppercase">How It Works</p>
            <h2 className="text-3xl md:text-4xl font-heading font-bold text-navy mb-4">
              From practice to offer
              <br />
              in four simple steps
            </h2>
          </div>

          <div className="grid md:grid-cols-2 lg:grid-cols-4 gap-8">
            {steps.map((s, i) => (
              <div key={s.num} className="relative">
                {i < steps.length - 1 && (
                  <div className="hidden lg:block absolute top-8 left-full w-full h-px bg-border z-0" />
                )}
                <div className="relative z-10">
                  <div className="w-16 h-16 rounded-full bg-primary/10 flex items-center justify-center mb-5">
                    <span className="text-xl font-heading font-bold text-primary">{s.num}</span>
                  </div>
                  <h3 className="text-lg font-heading font-semibold text-navy mb-2">{s.title}</h3>
                  <p className="text-sm text-text-body leading-relaxed">{s.desc}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ===== TESTIMONIALS ===== */}
      <section id="testimonials" className="py-20 md:py-28">
        <div className="max-w-6xl mx-auto px-6">
          <div className="text-center max-w-2xl mx-auto mb-16">
            <p className="text-sm font-semibold text-primary mb-3 tracking-wide uppercase">Testimonials</p>
            <h2 className="text-3xl md:text-4xl font-heading font-bold text-navy mb-4">
              Loved by job seekers
              <br />
              worldwide
            </h2>
          </div>

          <div className="grid md:grid-cols-3 gap-6">
            {testimonials.map((t) => (
              <div key={t.name} className="testimonial-card">
                <div className="stars mb-4">
                  {[...Array(t.rating)].map((_, i) => (
                    <svg key={i} width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
                      <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
                    </svg>
                  ))}
                </div>
                <p className="text-sm text-text-body leading-relaxed mb-6">&ldquo;{t.text}&rdquo;</p>
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-full bg-gradient-to-br from-primary to-primary-dark flex items-center justify-center text-white text-sm font-semibold">
                    {t.name.charAt(0)}
                  </div>
                  <div>
                    <p className="text-sm font-semibold text-navy">{t.name}</p>
                    <p className="text-xs text-text-muted">{t.role}</p>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ===== PRICING ===== */}
      <section id="pricing" className="py-20 md:py-28 bg-bg-light">
        <div className="max-w-6xl mx-auto px-6">
          <div className="text-center max-w-2xl mx-auto mb-16">
            <p className="text-sm font-semibold text-primary mb-3 tracking-wide uppercase">Pricing</p>
            <h2 className="text-3xl md:text-4xl font-heading font-bold text-navy mb-4">
              Invest in your career
            </h2>
            <p className="text-text-body text-lg">
              One-time payment. Lifetime access. No subscriptions.
            </p>
          </div>

          <div className="max-w-lg mx-auto">
            <div className="pricing-card">
              <div className="text-center mb-8">
                <p className="text-sm font-semibold text-primary mb-2 uppercase tracking-wide">VoxMentor Pro</p>
                <div className="flex items-baseline justify-center gap-1 mb-2">
                  <span className="text-5xl font-heading font-bold text-navy">$49</span>
                  <span className="text-text-muted text-sm">one-time</span>
                </div>
                <p className="text-sm text-text-muted">or 2 payments of $27</p>
              </div>

              <ul className="space-y-3 mb-8">
                {[
                  "Unlimited mock interviews",
                  "AI-powered instant feedback",
                  "50+ job role templates",
                  "Progress tracking & analytics",
                  "STAR answer builder",
                  "Community access",
                  "Lifetime updates",
                ].map((item) => (
                  <li key={item} className="flex items-center gap-3 text-sm text-text-body">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#10B981" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                      <polyline points="20 6 9 17 4 12" />
                    </svg>
                    {item}
                  </li>
                ))}
              </ul>

              <Link href={primaryCtaHref} className="btn-primary w-full text-center">
                Get VoxMentor Pro — $49
              </Link>
              <p className="text-center text-xs text-text-muted mt-4 flex items-center justify-center gap-1">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <rect width="18" height="11" x="3" y="11" rx="2" ry="2" />
                  <path d="M7 11V7a5 5 0 0 1 10 0v4" />
                </svg>
                30-Day Money-Back Guarantee
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* ===== CTA ===== */}
      <section className="py-20 md:py-28">
        <div className="max-w-6xl mx-auto px-6">
          <div className="relative overflow-hidden rounded-card-lg bg-gradient-to-br from-navy via-navy-light to-primary p-10 md:p-16 text-center">
            <div className="absolute inset-0 opacity-20">
              <div className="absolute inset-0" style={{
                backgroundImage: 'radial-gradient(circle at 20% 50%, rgba(125,211,232,0.4) 0%, transparent 50%), radial-gradient(circle at 80% 20%, rgba(214,240,250,0.3) 0%, transparent 50%)'
              }} />
            </div>
            <div className="relative z-10">
              <h2 className="text-3xl md:text-4xl font-heading font-bold text-white mb-4">
                Ready to ace your interview?
              </h2>
              <p className="text-blue-200/80 text-lg max-w-lg mx-auto mb-8">
                Join 10,000+ professionals who prepared with VoxMentor and landed their dream jobs.
              </p>
              <Link href={primaryCtaHref} className="btn-primary !bg-white !text-navy hover:!shadow-glow">
                {primaryCtaText}
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M5 12h14" />
                  <path d="m12 5 7 7-7 7" />
                </svg>
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* ===== FOOTER ===== */}
      <footer className="border-t border-border bg-bg-light py-12">
        <div className="max-w-6xl mx-auto px-6">
          <div className="flex flex-col md:flex-row items-center justify-between gap-6">
            <div className="flex items-center gap-3">
              <div className="logo-icon !w-8 !h-8 !rounded-lg">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
                  <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                  <line x1="12" x2="12" y1="19" y2="22" />
                </svg>
              </div>
              <span className="font-heading font-bold text-navy">VoxMentor</span>
            </div>
            <div className="flex items-center gap-6 text-sm text-text-muted">
              <a href="#features" className="hover:text-primary transition-colors">Features</a>
              <a href="#pricing" className="hover:text-primary transition-colors">Pricing</a>
              <span className="text-text-muted/60">Privacy</span>
              <span className="text-text-muted/60">Terms</span>
            </div>
            <p className="text-sm text-text-muted">
              &copy; {new Date().getFullYear()} VoxMentor. All rights reserved.
            </p>
          </div>
        </div>
      </footer>
    </div>
  );
}
