import { describe, expect, it } from "vitest";
import { cut, pageTitle, seoWarnings } from "./seo-check";

const ok = { title: "Backpack | Shop", description: "x".repeat(100), imageCount: 1, imagesWithoutAlt: 0, translated: true };

describe("seoWarnings", () => {
  it("has nothing to say about a complete page", () => {
    expect(seoWarnings(ok)).toEqual([]);
  });

  it("warns about long titles, missing or badly sized descriptions and images", () => {
    expect(seoWarnings({ ...ok, title: "t".repeat(61) })).toEqual(["titleLong"]);
    expect(seoWarnings({ ...ok, description: " " })).toEqual(["descriptionMissing"]);
    expect(seoWarnings({ ...ok, description: "short" })).toEqual(["descriptionShort"]);
    expect(seoWarnings({ ...ok, description: "d".repeat(161) })).toEqual(["descriptionLong"]);
    expect(seoWarnings({ ...ok, imageCount: 0 })).toEqual(["noImage"]);
    expect(seoWarnings({ ...ok, imagesWithoutAlt: 2 })).toEqual(["imageAltMissing"]);
  });

  it("only says 'not translated' for a language without a name", () => {
    expect(seoWarnings({ ...ok, translated: false, imageCount: 0 })).toEqual(["notTranslated"]);
  });
});

describe("cut and pageTitle", () => {
  it("cuts like a result list and adds the shop name", () => {
    expect(cut("  a   b  ", 10)).toBe("a b");
    expect(cut("abcdefghijk", 5)).toBe("abcd…");
    expect(pageTitle("Bag", "Shop")).toBe("Bag | Shop");
    expect(pageTitle("Bag", "")).toBe("Bag");
  });
});
