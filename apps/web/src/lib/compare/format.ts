import type { components } from "@/lib/api/schema";
import { formatList } from "@/lib/i18n/format-list";
import { formatNumber } from "@/lib/i18n/format-money";

export type CompareValue = components["schemas"]["CompareValue"];
export type CompareRow = components["schemas"]["CompareRow"];
export type CompareGroup = components["schemas"]["CompareGroup"];

type Texts = { yes: string; no: string; noValue: string };

/** A comparison cell as the reader sees it: "۱۲۸ GB", "Yes", "Black, White", or "—" when the product has none. */
export function formatCompareValue(value: CompareValue, row: Pick<CompareRow, "kind" | "unit">, locale: string, texts: Texts): string {
  if (row.kind === "Number" && value.number != null) {
    const number = formatNumber(value.number, locale);
    return row.unit ? `${number} ${row.unit}` : number;
  }
  if (row.kind === "Boolean" && value.boolean != null) return value.boolean ? texts.yes : texts.no;
  if (value.texts.length > 0) return formatList(value.texts, locale);
  return texts.noValue;
}

/** The groups to show: every row, or only the rows whose values differ (empty groups left out). */
export function visibleGroups(groups: readonly CompareGroup[], onlyDifferences: boolean): CompareGroup[] {
  if (!onlyDifferences) return [...groups];
  return groups.map((g) => ({ ...g, rows: g.rows.filter((r) => r.differs) })).filter((g) => g.rows.length > 0);
}
