import { defaultBranding, type Branding } from "@/lib/store/settings";

const radius: Record<Branding["corners"], string> = { Sharp: "0.25rem", Soft: "0.75rem", Round: "1.25rem" };

/** Relative luminance of a #rrggbb colour (WCAG formula). */
export function luminance(hex: string): number {
  const channel = (i: number) => {
    const c = parseInt(hex.slice(1 + i * 2, 3 + i * 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(0) + 0.7152 * channel(1) + 0.0722 * channel(2);
}

/** Text colour on the main colour: white on dark colours, near-black on light ones. */
export function foregroundFor(hex: string): string {
  // Contrast with white vs. with #111: pick the larger.
  const l = luminance(hex);
  const onWhite = 1.05 / (l + 0.05);
  const onBlack = (l + 0.05) / (luminance("#111111") + 0.05);
  return onWhite >= onBlack ? "#ffffff" : "#111111";
}

/**
 * CSS overriding the design tokens with the shop's branding, or null when nothing differs from the default (the
 * tokens in globals.css already match). The colour has been validated as #rrggbb (toBranding), so the string is safe
 * to put in a <style> element. Dark theme: a lighter tint of the main colour with dark text, for both the chosen dark
 * theme and the no-JavaScript system fallback.
 */
export function brandingCss(branding: Branding): string | null {
  const rules: string[] = [];
  const color = branding.primaryColor;

  if (color !== defaultBranding.primaryColor) {
    const dark = `--primary:color-mix(in srgb,${color} 55%,#ffffff);--primary-foreground:#0b1210;`;
    rules.push(`:root{--primary:${color};--primary-foreground:${foregroundFor(color)};}`);
    rules.push(`:root[data-theme="dark"]{${dark}}`);
    rules.push(`@media (prefers-color-scheme:dark){:root:not([data-theme]){${dark}}}`);
  }
  if (branding.corners !== defaultBranding.corners) rules.push(`:root{--radius:${radius[branding.corners]};}`);
  if (branding.font === "System") rules.push(`:root,:root:lang(hy){--font-script:system-ui,"Segoe UI",Tahoma;}`);

  return rules.length > 0 ? rules.join("") : null;
}

/** Home page sections in the order of the chosen layout template. */
export type HomeSection = "hero" | "quickLinks" | "deals" | "categories" | "newest" | "benefits";

export const homeLayouts: Record<Branding["homeLayout"], HomeSection[]> = {
  Classic: ["hero", "quickLinks", "deals", "categories", "newest", "benefits"],
  Catalog: ["categories", "deals", "newest", "benefits"],
  Showcase: ["hero", "newest", "categories", "deals", "benefits"],
};
