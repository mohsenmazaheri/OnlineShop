import { connection } from "next/server";
import { siteUrl } from "@/lib/seo/config";
import { sitemapIndexXml } from "@/lib/seo/sitemap";

/** The sitemap index robots.txt points to: one part per kind of page (/sitemaps/sitemap/{id}.xml). */
export async function GET(): Promise<Response> {
  await connection();
  return new Response(sitemapIndexXml(siteUrl()), { headers: { "Content-Type": "application/xml; charset=utf-8", "Cache-Control": "public, max-age=3600" } });
}
