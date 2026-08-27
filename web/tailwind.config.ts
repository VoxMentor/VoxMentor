import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
    "./components/**/*.{js,ts,jsx,tsx,mdx}",
    "./lib/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        background: "var(--background)",
        foreground: "var(--foreground)",
        primary: {
          DEFAULT: "#4AADDB",
          dark: "#3A9CC9",
          light: "#7DD3E8",
          glow: "rgba(74, 173, 219, 0.30)",
        },
        navy: {
          DEFAULT: "#1A2B4A",
          light: "#2D4A6F",
          deep: "#0F1D35",
        },
        "bg-light": "#F7F9FC",
        "bg-card": "#FFFFFF",
        "text-body": "#4A5568",
        "text-muted": "#718096",
        "text-heading": "#1A2B4A",
        border: {
          DEFAULT: "#E8EDF2",
          focus: "#4AADDB",
        },
        accent: {
          gold: "#F5A623",
          teal: "#7DD3E8",
          "light-blue": "#D6F0FA",
        },
        success: "#10B981",
        danger: "#EF4444",
      },
      fontFamily: {
        heading: ["var(--font-poppins)", "system-ui", "sans-serif"],
        body: ["var(--font-inter)", "system-ui", "sans-serif"],
      },
      borderRadius: {
        pill: "50px",
        card: "16px",
        "card-lg": "24px",
        input: "14px",
      },
      boxShadow: {
        soft: "0 2px 8px rgba(26, 43, 74, 0.06)",
        medium: "0 4px 16px rgba(26, 43, 74, 0.10)",
        card: "0 8px 30px rgba(74, 173, 219, 0.12)",
        "card-hover": "0 12px 40px rgba(74, 173, 219, 0.18)",
        float: "0 12px 40px rgba(26, 43, 74, 0.15)",
        glow: "0 4px 16px rgba(74, 173, 219, 0.30)",
      },
      animation: {
        "slide-up": "slideUp 0.6s cubic-bezier(0.16, 1, 0.3, 1) forwards",
        "fade-in": "fadeIn 0.4s ease forwards",
        float: "float 8s ease-in-out infinite",
      },
      keyframes: {
        slideUp: {
          "0%": { opacity: "0", transform: "translateY(20px)" },
          "100%": { opacity: "1", transform: "translateY(0)" },
        },
        fadeIn: {
          "0%": { opacity: "0" },
          "100%": { opacity: "1" },
        },
        float: {
          "0%, 100%": { transform: "translate(0, 0) scale(1)" },
          "33%": { transform: "translate(30px, -30px) scale(1.05)" },
          "66%": { transform: "translate(-20px, 20px) scale(0.95)" },
        },
      },
    },
  },
  plugins: [],
};
export default config;
