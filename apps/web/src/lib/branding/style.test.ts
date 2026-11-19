import { describe, expect, it } from "vitest";
import { defaultBranding, toBranding } from "@/lib/store/settings";
import { brandingCss, foregroundFor, homeLayouts } from "./style";

describe("brandingCss", () => {
  it("writes nothing for the default look", () => {
    expect(brandingCss(defaultBranding)).toBeNull();
  });

  it("overrides the main colour in light and dark, with readable text", () => {
    const css = brandingCss({ ...defaultBranding, primaryColor: "#1d4ed8" })!;

    expect(css).toContain(":root{--primary:#1d4ed8;--primary-foreground:#ffffff;}");
    expect(css).toContain(':root[data-theme="dark"]{--primary:color-mix(in srgb,#1d4ed8 55%,#ffffff)');
    expect(css).toContain("@media (prefers-color-scheme:dark){:root:not([data-theme])");
  });

  it("sets corners and the system font", () => {
    const css = brandingCss({ ...defaultBranding, corners: "Round", font: "System" })!;

    expect(css).toContain("--radius:1.25rem");
    expect(css).toContain("--font-script:system-ui");
    expect(css).not.toContain("--primary");
  });
});

describe("foregroundFor", () => {
  it("picks white on dark and near-black on light colours", () => {
    expect(foregroundFor("#0f766e")).toBe("#ffffff");
    expect(foregroundFor("#1d4ed8")).toBe("#ffffff");
    expect(foregroundFor("#f2a541")).toBe("#111111");
    expect(foregroundFor("#ffff00")).toBe("#111111");
  });
});

describe("toBranding", () => {
  it("keeps valid values and lower-cases the colour", () => {
    expect(toBranding({ primaryColor: "#AA3300", corners: "Sharp", font: "System", homeLayout: "Catalog" })).toEqual({
      primaryColor: "#aa3300",
      corners: "Sharp",
      font: "System",
      homeLayout: "Catalog",
      logoUrl: null,
      heroImageUrl: null,
    });
  });

  it("accepts only the API's own media addresses as logo and banner", () => {
    const key = "0123456789abcdef0123456789abcdef";
    expect(toBranding({ logoUrl: `/api/v1/media/${key}/300.webp`, heroImageUrl: "javascript:alert(1)" })).toMatchObject({
      logoUrl: `/api/v1/media/${key}/300.webp`,
      heroImageUrl: null,
    });
  });

  it("falls back to the default for anything unknown or unsafe", () => {
    expect(toBranding({ primaryColor: "red;}body{display:none", corners: "Huge", font: 3, homeLayout: null })).toEqual(defaultBranding);
    expect(toBranding(null)).toEqual(defaultBranding);
  });
});

describe("homeLayouts", () => {
  it("every template ends with the benefits and shows the categories", () => {
    for (const sections of Object.values(homeLayouts)) {
      expect(sections.at(-1)).toBe("benefits");
      expect(sections).toContain("categories");
    }
  });
});
