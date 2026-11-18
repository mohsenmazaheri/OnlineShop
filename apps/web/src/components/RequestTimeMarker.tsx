import { connection } from "next/server";

/**
 * Renders nothing; marks a page that has no per-request content of its own as rendered at request time. The layout's
 * metadata reads the store settings per request, and with Cache Components Next.js then needs such a marker inside
 * <Suspense> on the page (otherwise: "encountered uncached data in generateMetadata()").
 */
export async function RequestTimeMarker() {
  await connection();
  return null;
}
