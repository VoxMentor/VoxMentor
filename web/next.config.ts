import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  async rewrites() {
    const destination = process.env.BACKEND_ORIGIN;

    if (!destination) {
      return [];
    }

    return [
      {
        source: "/api/:path*",
        destination: `${destination}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
