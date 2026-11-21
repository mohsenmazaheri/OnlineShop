// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ContentArticle } from "@/lib/api/storefront";
import en from "@/lib/i18n/dictionaries/en.json";

vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

import { Article } from "./Article";

afterEach(cleanup);

const eur = { code: "EUR", symbol: null, decimals: 2, roundingStep: 0.01, rate: 1, isBase: true, base: "EUR" };
const post: ContentArticle = {
  kind: "Post",
  title: "Phone buying guide",
  slug: "phone-buying-guide",
  summary: "What to look at.",
  body: "## Screen\n\nBigger is **not** always better.",
  metaTitle: null,
  metaDescription: null,
  coverImageUrl: "/api/v1/media/abc/1200.webp",
  authorName: "Sara",
  tags: ["phones"],
  publishedUtc: "2026-11-20T10:00:00Z",
  modifiedUtc: "2026-11-20T10:00:00Z",
  alternates: [],
  relatedProducts: [
    { number: 1001, name: "Orvia X1", slug: "orvia-x1", imageUrl: null, price: 300, compareAtPrice: null, inStock: true, categorySlug: "phones", ratingAverage: 0, ratingCount: 0, saleEndsUtc: null },
  ],
};

describe("Article", () => {
  it("shows a post with its author, cover, text, tags and products", () => {
    render(<Article article={post} locale="en" dict={en} money={eur} />);
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Phone buying guide");
    expect(screen.getByText("By Sara")).toBeTruthy();
    expect(screen.getByRole("heading", { level: 2, name: "Screen" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "#phones" }).getAttribute("href")).toBe("/en/blog?tag=phones");
    expect(screen.getByRole("link", { name: en.blog.title }).getAttribute("href")).toBe("/en/blog");
    expect(screen.getByRole("heading", { name: en.blog.related })).toBeTruthy();
    expect(screen.getByText("Orvia X1")).toBeTruthy();
  });

  it("shows a page without the blog's breadcrumb, author or date", () => {
    render(<Article article={{ ...post, kind: "Page", tags: [], relatedProducts: [] }} locale="en" dict={en} money={eur} />);
    expect(screen.queryByRole("link", { name: en.blog.title })).toBeNull();
    expect(screen.queryByText("By Sara")).toBeNull();
    expect(screen.queryByRole("heading", { name: en.blog.related })).toBeNull();
  });
});
