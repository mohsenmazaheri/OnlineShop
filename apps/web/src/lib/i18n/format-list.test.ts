import { describe, expect, it } from "vitest";
import { formatList } from "./format-list";

describe("formatList", () => {
  it("uses each language's separator", () => {
    expect(formatList(["A", "B"], "en")).toBe("A, B");
    expect(formatList(["الف", "ب"], "fa")).toContain("،");
  });

  it("handles one or no items and unknown locales", () => {
    expect(formatList(["A"], "de")).toBe("A");
    expect(formatList([], "en")).toBe("");
    expect(formatList(["A", "B"], "not a locale!")).toBe("A, B");
  });
});
