import type { MetadataRoute } from "next";
import { getSitemapEntries, type SitemapKind } from "@/lib/api/storefront";
import { buildSitemap, entriesSitemap, sitemapIds, type SitemapId } from "@/lib/seo/sitemap";
import { siteUrl } from "@/lib/seo/config";
import { loadStore } from "@/lib/store/load";

const kinds: Record<Exclude<SitemapId, "pages">, SitemapKind> = {
  products: "Products",
  categories: "Categories",
  brands: "Brands",
  landing: "Landing",
  posts: "Posts",
  content: "Pages",
};

/** The parts of the sitemap index at /sitemap.xml: /sitemaps/sitemap/{id}.xml. */
export function generateSitemaps() {
  return sitemapIds.map((id) => ({ id }));
}

/**
 * One part: the home and blog pages, or the products (with images and videos), categories, brands, landing pages, blog
 * posts or content pages in every language they are translated into. Read per request (the store's languages and the
 * catalog are runtime data).
 */
export default async function sitemap({ id }: { id: Promise<string> }): Promise<MetadataRoute.Sitemap> {
  const part = (await id) as SitemapId;
  const store = await loadStore();
  if (part === "pages" || !(part in kinds)) return buildSitemap(["/", "/blog"], store.locales, store.defaultLocale);
  const entries = await getSitemapEntries(kinds[part as Exclude<SitemapId, "pages">]).catch(() => []);
  return entriesSitemap(entries, store.defaultLocale, siteUrl());
}
