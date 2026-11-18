import { describe, expect, it } from "vitest";
import { brandOf, formatSpecValue, groupSpecs, type SpecLine } from "./specs";

const line = (over: Partial<SpecLine>): SpecLine => ({
  code: "x",
  group: "General",
  name: "X",
  kind: "Text",
  unit: null,
  number: null,
  boolean: null,
  value: null,
  inSummary: false,
  ...over,
});

const texts = { yes: "Yes", no: "No" };

describe("formatSpecValue", () => {
  it("formats numbers with the unit in the reader's digits", () => {
    expect(formatSpecValue(line({ kind: "Number", number: 6.7, unit: "اینچ" }), "fa", texts)).toBe("۶٫۷ اینچ");
    expect(formatSpecValue(line({ kind: "Number", number: 2400, unit: "W" }), "en", texts)).toBe("2,400 W");
    expect(formatSpecValue(line({ kind: "Number", number: 8 }), "en", texts)).toBe("8");
  });

  it("turns yes/no into words and keeps text and options as they are", () => {
    expect(formatSpecValue(line({ kind: "Boolean", boolean: true }), "en", texts)).toBe("Yes");
    expect(formatSpecValue(line({ kind: "Boolean", boolean: false }), "en", texts)).toBe("No");
    expect(formatSpecValue(line({ kind: "Option", value: "Schwarz" }), "de", texts)).toBe("Schwarz");
    expect(formatSpecValue(line({ kind: "Text", value: "A5" }), "fa", texts)).toBe("A5");
  });

  it("gives an empty string for a missing value", () => {
    expect(formatSpecValue(line({ kind: "Number" }), "en", texts)).toBe("");
    expect(formatSpecValue(line({ kind: "Boolean" }), "en", texts)).toBe("");
  });
});

describe("groupSpecs and brandOf", () => {
  it("groups in order of first appearance", () => {
    const groups = groupSpecs([line({ code: "a", group: "General" }), line({ code: "b", group: "Display" }), line({ code: "c", group: "General" })]);

    expect(groups.map((g) => [g.group, g.lines.map((l) => l.code)])).toEqual([
      ["General", ["a", "c"]],
      ["Display", ["b"]],
    ]);
  });

  it("finds the brand", () => {
    expect(brandOf([line({ code: "brand", value: "Orvia" })])).toBe("Orvia");
    expect(brandOf([line({ code: "color", value: "Black" })])).toBeNull();
  });
});
