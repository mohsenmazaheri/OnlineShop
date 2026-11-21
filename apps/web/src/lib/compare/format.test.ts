import { describe, expect, it } from "vitest";
import { formatCompareValue, visibleGroups, type CompareGroup } from "./format";

const texts = { yes: "Yes", no: "No", noValue: "—" };

describe("comparison values", () => {
  it("formats numbers with units, yes/no, lists and missing values", () => {
    expect(formatCompareValue({ number: 128, boolean: null, texts: [] }, { kind: "Number", unit: "GB" }, "en", texts)).toBe("128 GB");
    expect(formatCompareValue({ number: 6.5, boolean: null, texts: [] }, { kind: "Number", unit: null }, "fa", texts)).toBe("۶٫۵");
    expect(formatCompareValue({ number: null, boolean: false, texts: [] }, { kind: "Boolean", unit: null }, "en", texts)).toBe("No");
    expect(formatCompareValue({ number: null, boolean: null, texts: ["Black", "White"] }, { kind: "Option", unit: null }, "en", texts)).toBe("Black, White");
    expect(formatCompareValue({ number: null, boolean: null, texts: [] }, { kind: "Number", unit: "GB" }, "en", texts)).toBe("—");
  });

  it("keeps only the rows that differ when asked", () => {
    const row = (code: string, differs: boolean) => ({ code, name: code, kind: "Text" as const, unit: null, better: "None" as const, values: [], best: [], differs });
    const groups: CompareGroup[] = [
      { name: "A", rows: [row("a1", true), row("a2", false)] },
      { name: "B", rows: [row("b1", false)] },
    ];
    expect(visibleGroups(groups, false)).toHaveLength(2);
    expect(visibleGroups(groups, true)).toEqual([{ name: "A", rows: [row("a1", true)] }]);
  });
});
