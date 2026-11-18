import type { ProductDetail } from "@/lib/api/storefront";
import { formatNumber } from "@/lib/i18n/format-money";

export type SpecLine = ProductDetail["specs"][number];
export type SpecTexts = { yes: string; no: string };

/** A spec value as the reader sees it: "۶٫۷ اینچ", "16 GB", "Yes", "Stainless steel". */
export function formatSpecValue(line: SpecLine, locale: string, texts: SpecTexts): string {
  switch (line.kind) {
    case "Number": {
      if (line.number == null) return "";
      const value = formatNumber(line.number, locale);
      return line.unit ? `${value} ${line.unit}` : value;
    }
    case "Boolean":
      return line.boolean == null ? "" : line.boolean ? texts.yes : texts.no;
    default:
      return line.value ?? "";
  }
}

/** Specs grouped under their headings, in the API's order (groups in order of first appearance). */
export function groupSpecs(lines: readonly SpecLine[]): { group: string; lines: SpecLine[] }[] {
  const groups = new Map<string, SpecLine[]>();
  for (const line of lines) {
    const list = groups.get(line.group) ?? [];
    list.push(line);
    groups.set(line.group, list);
  }
  return [...groups].map(([group, list]) => ({ group, lines: list }));
}

/** The brand, when the product has a "brand" spec. */
export function brandOf(lines: readonly SpecLine[]): string | null {
  return lines.find((l) => l.code === "brand")?.value ?? null;
}
