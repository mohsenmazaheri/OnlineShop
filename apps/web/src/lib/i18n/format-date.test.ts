import { describe, expect, it } from "vitest";
import { formatDate, formatYear } from "./format-date";

describe("formatYear", () => {
  const date = new Date("2026-11-16T09:30:00Z");

  it("uses the reader's calendar and digits", () => {
    expect(formatYear(date, "fa")).toBe("۱۴۰۵");
    expect(formatYear(date, "en")).toBe("2026");
  });

  it("falls back to the Gregorian year for an unknown locale", () => {
    expect(formatYear(date, "not a locale!")).toBe("2026");
  });
});

describe("formatDate", () => {
  const iso = "2026-11-16T09:30:00Z";

  it("uses the Persian calendar for fa", () => {
    expect(formatDate(iso, "fa")).toContain("۱۴۰۵");
  });

  it("uses the Gregorian calendar for other languages", () => {
    expect(formatDate(iso, "en")).toContain("2026");
    expect(formatDate(iso, "de")).toContain("2026");
  });

  it("adds the time on request", () => {
    expect(formatDate(iso, "en", true)).not.toBe(formatDate(iso, "en"));
  });

  it("leaves invalid input and unknown locales usable", () => {
    expect(formatDate("not a date", "en")).toBe("not a date");
    expect(formatDate(iso, "not a locale!")).toBe("2026-11-16");
  });
});
