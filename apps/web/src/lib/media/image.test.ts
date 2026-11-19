import { describe, expect, it } from "vitest";
import { imageVariant, isMediaUrl } from "./image";

const key = "0123456789abcdef0123456789abcdef";

describe("imageVariant", () => {
  it("switches the size of an uploaded image", () => {
    expect(imageVariant(`/api/v1/media/${key}/1200.webp`, 300)).toBe(`/api/v1/media/${key}/300.webp`);
    expect(imageVariant(`/api/v1/media/${key}/600.webp`, 1200)).toBe(`/api/v1/media/${key}/1200.webp`);
  });

  it("leaves other addresses alone", () => {
    expect(imageVariant("/demo/products/kettle.svg", 300)).toBe("/demo/products/kettle.svg");
    expect(imageVariant("https://cdn.example.com/a/1200.webp", 300)).toBe("https://cdn.example.com/a/1200.webp");
  });
});

describe("isMediaUrl", () => {
  it("accepts only API media addresses", () => {
    expect(isMediaUrl(`/api/v1/media/${key}/300.webp`)).toBe(true);
    expect(isMediaUrl(`/api/v1/media/${key}/999.webp`)).toBe(false);
    expect(isMediaUrl("javascript:alert(1)")).toBe(false);
    expect(isMediaUrl(null)).toBe(false);
  });
});
