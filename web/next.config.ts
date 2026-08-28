import type { NextConfig } from "next";

const getBackendOrigin = (): string => {
  const origin = process.env.BACKEND_ORIGIN;
  if (!origin) {
    throw new Error(
      "BACKEND_ORIGIN is not configured. Set it in your environment or .env file."
    );
  }
  return origin;
};

const nextConfig: NextConfig = {
  async rewrites() {
    const destination = getBackendOrigin();

    return [
      {
        source: "/api/:path*",
        destination: `${destination}/api/:path*`,
      },
      {
        source: "/hubs/:path*",
        destination: `${destination}/hubs/:path*`,
      },
    ];
  },
};

export default nextConfig;
