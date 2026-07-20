import type { NextConfig } from "next";

// Athlete/event images live on Cloudflare R2 in production and MinIO (localhost:9000)
// in dev. Set NEXT_PUBLIC_IMAGE_HOST to the R2 public hostname when deploying.
const imageHost = process.env.NEXT_PUBLIC_IMAGE_HOST;

const nextConfig: NextConfig = {
  images: {
    remotePatterns: [
      { protocol: "http", hostname: "localhost" },
      { protocol: "http", hostname: "127.0.0.1" },
      ...(imageHost
        ? [{ protocol: "https" as const, hostname: imageHost }]
        : []),
    ],
  },
};

export default nextConfig;
