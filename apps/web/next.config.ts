import type { NextConfig } from "next";

/*
 * Nothing here depends on the shop or the environment: next.config is fixed at build time, and one Docker image
 * serves every shop. Runtime concerns (API forwarding, security headers, locale redirects, noindex) live in src/proxy.ts.
 */
const nextConfig: NextConfig = {
  cacheComponents: true,
  partialPrefetching: true,
  // Self-contained server bundle for the Docker image (node server.js).
  output: "standalone",
  poweredByHeader: false,
  experimental: {
    // proxy.ts buffers request bodies (default 10 MB) before forwarding them to the API: room for a 50 MB product
    // video and its multipart envelope (nginx allows the same, see deploy/nginx.conf).
    proxyClientMaxBodySize: "52mb",
  },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
  async headers() {
    return [
      {
        // The service worker must always be revalidated so updates reach users quickly.
        source: "/sw.js",
        headers: [
          { key: "Cache-Control", value: "no-cache, no-store, must-revalidate" },
          { key: "Service-Worker-Allowed", value: "/" },
        ],
      },
    ];
  },
};

export default nextConfig;
