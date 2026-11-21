import { describe, expect, it } from "vitest";
import type { BrowseResult, LandingPageContent } from "@/lib/api/storefront";
import { entriesSitemap, sitemapIndexXml } from "./sitemap";
import { landingApiQuery, landingChoices, landingJsonLd, landingPath, landingSeo } from "./landing";

const site = "https://shop.example.com";

const page: LandingPageContent = {
  title: "Orvia phones",
  slug: "orvia-phones",
  intro: "The best ones.",
  metaTitle: null,
  metaDescription: null,
  query: "c=electronics&brand=orvia&sort=price_asc",
  alternates: [
    { locale: "fa", slug: "گوشی-اورویا" },
    { locale: "en", slug: "orvia-phones" },
  ],
  modifiedUtc: "2026-11-20T10:00:00Z",
};

describe("landing pages", () => {
  it("keep only the visitor's sort and page, on top of the fixed query", () => {
    const choices = landingChoices({ sort: "rating", page: "3", brand: "other", min: "1" });
    expect(choices).toEqual({ sort: "rating", page: 3 });
    expect(landingChoices({ sort: "<script>", page: "-4" })).toEqual({ sort: null, page: 1 });
    expect(landingApiQuery(page, "en", choices)).toBe("c=electronics&brand=orvia&sort=rating&locale=en&page=3");
    expect(landingApiQuery(page, "fa", { sort: null, page: 1 })).toBe("c=electronics&brand=orvia&sort=price_asc&locale=fa");
  });

  it("are indexed in their translated languages, with hreflang and a canonical per page", () => {
    const en = landingSeo(page, "en", "fa", site, { sort: null, page: 1 });
    expect(en).toEqual({
      canonical: `${site}/en/l/orvia-phones`,
      languages: { fa: `${site}${landingPath("fa", "گوشی-اورویا")}`, en: `${site}/en/l/orvia-phones`, "x-default": `${site}${landingPath("fa", "گوشی-اورویا")}` },
      indexable: true,
    });
    expect(landingSeo(page, "en", "fa", site, { sort: null, page: 2 }).canonical).toBe(`${site}/en/l/orvia-phones?page=2`);
    expect(landingSeo(page, "en", "fa", site, { sort: "rating", page: 1 }).indexable).toBe(false);
    const hy = landingSeo({ ...page, slug: "گوشی-اورویا" }, "hy", "fa", site, { sort: null, page: 1 });
    expect(hy.indexable).toBe(false);
    expect(hy.canonical).toBe(`${site}${landingPath("fa", "گوشی-اورویا")}`);
  });

  it("describe the shown products as an ItemList under a breadcrumb", () => {
    const result = { page: 2, pageSize: 24, total: 25, items: [{ number: 1001, name: "Orvia X1", slug: "orvia-x1" }] } as unknown as BrowseResult;
    const [crumbs, list] = landingJsonLd(page, result, "en", site, "Home", `${site}/en/l/orvia-phones`);
    expect(crumbs.itemListElement).toHaveLength(2);
    expect(list.itemListElement).toEqual([{ "@type": "ListItem", position: 25, url: `${site}/en/p/1001/orvia-x1`, name: "Orvia X1" }]);
  });
});

describe("sitemaps", () => {
  it("index one part per kind of page", () => {
    const xml = sitemapIndexXml(site);
    expect(xml).toContain("<sitemapindex");
    expect(xml).toContain(`<loc>${site}/sitemaps/sitemap/products.xml</loc>`);
    expect(xml.match(/<sitemap>/g)).toHaveLength(7);
  });

  it("list a page in each translated language with alternates, images and videos with a thumbnail", () => {
    const entries = entriesSitemap(
      [
        {
          links: [
            { locale: "fa", path: "p/1001/گوشی" },
            { locale: "en", path: "p/1001/phone" },
          ],
          modifiedUtc: "2026-11-20T10:00:00Z",
          images: ["/api/v1/media/abc/1200.webp"],
          videos: [
            { url: "/api/v1/media/v1/video.mp4", posterUrl: "/api/v1/media/p1/600.webp", title: "Unboxing", description: null, durationSeconds: 65.4 },
            { url: "/api/v1/media/v2/video.mp4", posterUrl: null, title: "No poster", description: null, durationSeconds: null },
          ],
        },
      ],
      "fa",
      site,
    );

    expect(entries.map((e) => e.url)).toEqual([`${site}/fa/p/1001/${encodeURIComponent("گوشی")}`, `${site}/en/p/1001/phone`]);
    expect(entries[1].alternates?.languages).toMatchObject({ "x-default": entries[0].url, en: entries[1].url });
    expect(entries[0].images).toEqual([`${site}/api/v1/media/abc/1200.webp`]);
    expect(entries[0].videos).toEqual([
      { title: "Unboxing", thumbnail_loc: `${site}/api/v1/media/p1/600.webp`, description: "Unboxing", content_loc: `${site}/api/v1/media/v1/video.mp4`, duration: 65 },
    ]);
    expect(entries[0].lastModified).toBe("2026-11-20T10:00:00Z");
  });
});
