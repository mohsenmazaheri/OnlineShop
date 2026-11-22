/**
 * Display currencies: which currency a visitor sees and how base prices are converted for it. Pure functions (no
 * request access), shared by pages, components and tests.
 */

/** A currency as the API offers it (rates are units per one unit of the base currency). */
export type PublicCurrency = {
  code: string;
  symbol: string | null;
  decimals: number;
  roundingStep: number;
  rate: number;
};

export type CurrencySettings = {
  base: string;
  currencies: PublicCurrency[];
  localeDefaults: Record<string, string>;
};

/** The currency prices are shown in on this request. */
export type DisplayCurrency = PublicCurrency & { isBase: boolean; base: string };

/** Cookie holding the visitor's choice, as "{locale}:{CODE}". */
export const currencyCookieName = "os_currency";

/**
 * The choice is remembered together with the language it was made in: after switching language the visitor first
 * sees that language's own currency (fa → IRR, en → USD, …), as the shop owner set it up.
 */
export function currencyCookieValue(locale: string, code: string): string {
  return `${locale}:${code.toUpperCase()}`;
}

export function parseCurrencyCookie(value: string | null | undefined): { locale: string; code: string } | null {
  const match = /^([a-z]{2}):([A-Z]{3})$/.exec(value ?? "");
  return match ? { locale: match[1], code: match[2] } : null;
}

/** The visitor's choice for this language if it is still offered, otherwise the language's default, otherwise the base. */
export function resolveDisplayCurrency(settings: CurrencySettings, locale: string, cookie: string | null | undefined): DisplayCurrency {
  const find = (code: string | undefined) => (code ? settings.currencies.find((c) => c.code === code) : undefined);
  const chosen = parseCurrencyCookie(cookie);
  const currency =
    (chosen?.locale === locale ? find(chosen.code) : undefined) ??
    find(settings.localeDefaults[locale]) ??
    find(settings.base) ?? { code: settings.base, symbol: null, decimals: 0, roundingStep: 1, rate: 1 };
  return { ...currency, isBase: currency.code === settings.base, base: settings.base };
}

/** The base currency itself, for amounts that are charged as they are (checkout, orders). */
export function baseCurrency(settings: CurrencySettings): DisplayCurrency {
  const found = settings.currencies.find((c) => c.code === settings.base);
  const decimals = found?.decimals ?? (["IRR", "IRT", "JPY", "KRW"].includes(settings.base) ? 0 : 2);
  return { ...(found ?? { code: settings.base, symbol: null, decimals, roundingStep: 1, rate: 1 }), rate: 1, isBase: true, base: settings.base };
}

/** A base-currency amount in the display currency, rounded to its step (the base currency is never changed). */
export function convertAmount(amount: number, currency: DisplayCurrency): number {
  if (currency.isBase) return amount;
  const step = currency.roundingStep > 0 ? currency.roundingStep : 0.01;
  const converted = Math.round((amount * currency.rate) / step) * step;
  // Remove floating-point noise from the multiplication (0.1 + 0.2 style), keeping the currency's decimals.
  return Number(converted.toFixed(Math.max(currency.decimals, decimalsOf(step))));
}

function decimalsOf(step: number): number {
  const text = String(step);
  const dot = text.indexOf(".");
  return dot < 0 ? 0 : text.length - dot - 1;
}

/** Whether search engines know the code (schema.org prices need ISO 4217; IRT for toman is shop-defined). */
export function isIsoCurrency(code: string): boolean {
  try {
    return (Intl.supportedValuesOf("currency") as string[]).includes(code);
  } catch {
    return false;
  }
}
