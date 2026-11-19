import { describe, expect, it } from "vitest";
import { convertAmount, currencyCookieValue, isIsoCurrency, parseCurrencyCookie, resolveDisplayCurrency, type CurrencySettings } from "./display";

const settings: CurrencySettings = {
  base: "IRR",
  currencies: [
    { code: "IRR", symbol: null, decimals: 0, roundingStep: 1000, rate: 1 },
    { code: "USD", symbol: null, decimals: 2, roundingStep: 0.01, rate: 0.000000518 },
    { code: "IRT", symbol: "تومان", decimals: 0, roundingStep: 1000, rate: 0.1 },
  ],
  localeDefaults: { fa: "IRR", en: "USD", de: "EUR" },
};

describe("currency cookie", () => {
  it("round-trips the language and the code", () => {
    expect(parseCurrencyCookie(currencyCookieValue("fa", "irt"))).toEqual({ locale: "fa", code: "IRT" });
  });

  it("ignores anything malformed", () => {
    for (const value of [null, "", "USD", "fa:usd", "fa:USDX", "<x>:USD"]) expect(parseCurrencyCookie(value)).toBeNull();
  });
});

describe("resolveDisplayCurrency", () => {
  it("uses the visitor's choice made in the same language", () => {
    expect(resolveDisplayCurrency(settings, "fa", "fa:IRT")).toMatchObject({ code: "IRT", isBase: false, base: "IRR" });
  });

  it("starts with the language's own currency after a language switch", () => {
    expect(resolveDisplayCurrency(settings, "en", "fa:IRT").code).toBe("USD");
  });

  it("falls back to the base currency for an unknown choice or a missing default", () => {
    expect(resolveDisplayCurrency(settings, "fa", "fa:GBP")).toMatchObject({ code: "IRR", isBase: true });
    expect(resolveDisplayCurrency(settings, "de", null).code).toBe("IRR");
    expect(resolveDisplayCurrency({ base: "EUR", currencies: [], localeDefaults: {} }, "de", null)).toMatchObject({ code: "EUR", rate: 1 });
  });
});

describe("convertAmount", () => {
  it("converts and rounds to the currency's step", () => {
    const usd = resolveDisplayCurrency(settings, "en", null);
    const toman = resolveDisplayCurrency(settings, "fa", "fa:IRT");

    expect(convertAmount(349_000_000, usd)).toBe(180.78);
    expect(convertAmount(349_000_000, toman)).toBe(34_900_000);
    expect(convertAmount(15_120_000, toman)).toBe(1_512_000);
  });

  it("never changes base prices", () => {
    expect(convertAmount(349_000_123, resolveDisplayCurrency(settings, "fa", null))).toBe(349_000_123);
  });
});

describe("isIsoCurrency", () => {
  it("knows ISO codes and not shop-defined ones", () => {
    expect(isIsoCurrency("USD")).toBe(true);
    expect(isIsoCurrency("IRT")).toBe(false);
  });
});
