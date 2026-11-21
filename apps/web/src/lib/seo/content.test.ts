import { describe, expect, it } from "vitest";
import type { ContentArticle } from "@/lib/api/storefront";
import { contentJsonLd, contentPath, contentSeo } from "./content";

const site = "https://shop.example.com";

const post: ContentArticle = {
  kind: "Post",
  title: "Phone buying guide",
  slug: "phone-buying-guide",
  summary: "What to look at.",
  body: "Text",
  metaTitle: null,
  metaDescription: null,
  coverImageUrl: "/api/v1/media/abc/1200.webp",
  authorName: "Sara",
  tags: ["phones", "tips"],
  publishedUtc: "2026-11-20T10:00:00Z",
  modifiedUtc: "2026-11-21T10:00:00Z",
  alternates: [
    { locale: "fa", slug: "راهنمای-خرید" },
    { locale: "en", slug: "phone-buying-guide" },
  ],
  relatedProducts: [],
};

describe("content SEO", () => {
  it("puts posts under /blog and pages at the top level", () => {
    expect(contentPath("Post", "en", "guide")).toBe("/en/blog/guide");
    expect(contentPath("Page", "fa", "درباره")).toBe(`/fa/${encodeURIComponent("درباره")}`);
  });

  it("is indexed in translated languages only, with hreflang", () => {
    const en = contentSeo(post, "en", "fa", site);
    expect(en.indexable).toBe(true);
    expect(en.canonical).toBe(`${site}/en/blog/phone-buying-guide`);
    expect(en.languages["x-default"]).toBe(`${site}${contentPath("Post", "fa", "راهنمای-خرید")}`);
    const hy = contentSeo({ ...post, slug: "راهنمای-خرید" }, "hy", "fa", site);
    expect(hy).toMatchObject({ indexable: false, canonical: en.languages["x-default"] });
  });

  it("describes a post as a BlogPosting and a page as a WebPage", () => {
    const crumbs = [{ name: "Home", url: `${site}/en` }];
    const [article, breadcrumb] = contentJsonLd(post, "en", site, `${site}/en/blog/phone-buying-guide`, crumbs, "Shop");
    expect(article).toMatchObject({
      "@type": "BlogPosting",
      headline: "Phone buying guide",
      image: [`${site}/api/v1/media/abc/1200.webp`],
      datePublished: "2026-11-20T10:00:00Z",
      author: { "@type": "Person", name: "Sara" },
      keywords: "phones, tips",
      inLanguage: "en",
    });
    expect(breadcrumb).toMatchObject({ "@type": "BreadcrumbList" });
    const [page] = contentJsonLd({ ...post, kind: "Page", authorName: null }, "en", site, `${site}/en/about`, crumbs, "Shop");
    expect(page).toMatchObject({ "@type": "WebPage", name: "Phone buying guide", url: `${site}/en/about` });
  });
});
