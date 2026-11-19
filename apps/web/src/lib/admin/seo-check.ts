/** Lengths search engines usually show (characters); longer text is cut with "…". */
export const titleLimit = 60;
export const descriptionLimit = 160;
const descriptionMinimum = 70;

export type SeoWarning = "titleLong" | "descriptionMissing" | "descriptionShort" | "descriptionLong" | "noImage" | "imageAltMissing" | "notTranslated";

export type SeoInput = {
  /** The page title as search results show it (with the shop name the layout adds). */
  title: string;
  /** Meta description, else the product description. */
  description: string;
  imageCount: number;
  imagesWithoutAlt: number;
  /** False for a non-default language without a translated name (that page is not indexed). */
  translated: boolean;
};

/** What to improve before search engines show the page (docs/SEO-MARKETING.md §4). */
export function seoWarnings(input: SeoInput): SeoWarning[] {
  if (!input.translated) return ["notTranslated"];
  const warnings: SeoWarning[] = [];
  if (input.title.length > titleLimit) warnings.push("titleLong");
  const description = input.description.trim();
  if (!description) warnings.push("descriptionMissing");
  else if (description.length < descriptionMinimum) warnings.push("descriptionShort");
  else if (description.length > descriptionLimit) warnings.push("descriptionLong");
  if (input.imageCount === 0) warnings.push("noImage");
  else if (input.imagesWithoutAlt > 0) warnings.push("imageAltMissing");
  return warnings;
}

/** Text cut the way a result list cuts it. */
export function cut(text: string, limit: number): string {
  const clean = text.replace(/\s+/g, " ").trim();
  return clean.length > limit ? `${clean.slice(0, limit - 1).trimEnd()}…` : clean;
}

/** The page title as the layout writes it ("{title} | {shop}"). */
export function pageTitle(title: string, shopName: string): string {
  return shopName ? `${title} | ${shopName}` : title;
}
