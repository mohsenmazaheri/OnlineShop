import { describe, expect, it } from "vitest";
import { discountShare, formatMoney, formatNumber, formatPercent } from "./format-money";

describe("formatMoney", () => {
  it("writes the amount first in Persian and Arabic, with the reader's digits", () => {
    expect(formatMoney(349_000_000, "IRR", "fa")).toBe("۳۴۹٬۰۰۰٬۰۰۰ ریال");
    expect(formatMoney(383.9, "USD", "ar")).toMatch(/^383\.90 US\$$/);
  });

  it("uses the usual layout elsewhere and drops .00 on whole amounts", () => {
    expect(formatMoney(349, "EUR", "en")).toBe("€349");
    expect(formatMoney(383.9, "EUR", "en")).toBe("€383.90");
    expect(formatMoney(1249, "EUR", "de")).toMatch(/^1\.249\s€$/);
    expect(formatMoney(150_100, "AMD", "hy")).toMatch(/^150\s100\s֏$/);
  });

  it("shows a shop-defined symbol after the amount, in any language", () => {
    expect(formatMoney(34_900_000, "IRT", "fa", { symbol: "تومان" })).toBe("۳۴٬۹۰۰٬۰۰۰ تومان");
    expect(formatMoney(12.5, "IRT", "en", { symbol: "Toman", decimals: 1 })).toBe("12.5 Toman");
  });

  it("writes an unknown code like a symbol and never throws", () => {
    expect(formatMoney(5, "IRT", "en")).toBe("IRT 5");
    expect(formatMoney(5, "IRT", "fa")).toBe("۵ IRT");
    expect(formatMoney(5, "not money", "en")).toBe("not money 5");
  });
});

describe("formatPercent and formatNumber", () => {
  it("use the reader's digits", () => {
    expect(formatPercent(0.2, "fa")).toBe("۲۰٪");
    expect(formatPercent(0.255, "en")).toBe("26%");
    expect(formatNumber(6.7, "fa")).toBe("۶٫۷");
    expect(formatNumber(2400, "de")).toBe("2.400");
  });
});

describe("discountShare", () => {
  it("is the saved share of the old price", () => {
    expect(discountShare(80, 100)).toBeCloseTo(0.2);
  });

  it("is null without a real discount", () => {
    expect(discountShare(100, null)).toBeNull();
    expect(discountShare(100, 100)).toBeNull();
    expect(discountShare(100, 90)).toBeNull();
  });
});
