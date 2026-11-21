import type { MetadataRoute } from "next";
import type { Locale } from "@/lib/i18n/config";
import { buildAlternates } from "./config";

/**
 * One sitemap entry per page and locale, each listing its hreflang alternates.
 * @param paths paths after the locale ("/" for the home page)
 */
export function buildSitemap(
  paths: readonly string[],
  locales: readonly Locale[],
  fallbackLocale: Locale,
  env: Record<string, string | undefined> = process.env,
): MetadataRoute.Sitemap {
  return paths.flatMap((path) =>
    locales.map((locale) => {
      const alternates = buildAlternates(path, locale, locales, fallbackLocale, env);
      return {
        url: alternates.canonical as string,
        alternates: { languages: alternates.languages as Record<string, string> },
      };
    }),
  );
}

/** A page of the API's sitemap data (one per page, with its address in each translated language). */
export type SitemapSource = {
  links: { locale: string; path: string }[];
  modifiedUtc: string;
  images: string[];
  videos: { url: string; posterUrl: string | null; title: string; description: string | null; durationSeconds: number | null }[];
};

/** The sitemap index's parts: home pages, then one sitemap per kind of page. */
export const sitemapIds = ["pages", "products", "categories", "brands", "landing", "posts", "content"] as const;
export type SitemapId = (typeof sitemapIds)[number];

/** The sitemap index (docs/SEO-MARKETING.md §6): the address of every part. */
export function sitemapIndexXml(site: string): string {
  const parts = sitemapIds.map((id) => `  <sitemap><loc>${site}/sitemaps/sitemap/${id}.xml</loc></sitemap>`).join("\n");
  return `<?xml version="1.0" encoding="UTF-8"?>\n<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${parts}\n</sitemapindex>\n`;
}

/**
 * One sitemap entry per page and language it is translated into, each listing the page's other languages (and
 * x-default, the default language). Products carry their images and videos; a video needs a thumbnail, so one without
 * a poster or image is left out of the video extension.
 */
export function entriesSitemap(pages: readonly SitemapSource[], defaultLocale: string, site: string): MetadataRoute.Sitemap {
  const url = (locale: string, path: string) => `${site}/${locale}/${path.split("/").map(encodeURIComponent).join("/")}`;
  const absolute = (path: string) => (/^https?:\/\//.test(path) ? path : `${site}${path.startsWith("/") ? "" : "/"}${path}`);
  return pages.flatMap((page) => {
    const languages: Record<string, string> = Object.fromEntries(page.links.map((l) => [l.locale, url(l.locale, l.path)]));
    const fallback = page.links.find((l) => l.locale === defaultLocale) ?? page.links[0];
    if (fallback) languages["x-default"] = url(fallback.locale, fallback.path);
    const images = page.images.map(absolute);
    const videos = page.videos
      .filter((v) => v.posterUrl)
      .map((v) => ({
        title: v.title,
        thumbnail_loc: absolute(v.posterUrl!),
        description: (v.description || v.title).slice(0, 2048),
        content_loc: absolute(v.url),
        ...(v.durationSeconds ? { duration: Math.round(v.durationSeconds) } : {}),
      }));
    return page.links.map((link) => ({
      url: url(link.locale, link.path),
      lastModified: page.modifiedUtc,
      alternates: { languages },
      ...(images.length > 0 ? { images } : {}),
      ...(videos.length > 0 ? { videos } : {}),
    }));
  });
}
