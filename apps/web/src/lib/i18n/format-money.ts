/**
 * Money in the reader's language, from the runtime's built-in ICU data (`Intl.NumberFormat`): Persian digits for fa,
 * the currency's symbol or code, no ".00" on whole amounts. Right-to-left languages get the amount first and the
 * currency after it ("۳۴۹٬۰۰۰٬۰۰۰ ریال"), as Persian and Arabic shops write prices.
 */
export function formatMoney(amount: number, currency: string, locale: string, options: MoneyOptions = {}): string {
  const whole = Number.isInteger(amount);
  // A shop-defined symbol ("تومان") or currency (IRT): the number in the reader's digits plus that symbol.
  if (options.symbol || !isKnownCurrency(currency)) {
    const digits = whole ? 0 : (options.decimals ?? 2);
    const number = formatNumber(amount, locale, digits);
    const symbol = options.symbol || currency;
    return rtlLocales.has(locale.split("-")[0]) || options.symbol ? `${number} ${symbol}` : `${symbol} ${number}`;
  }

  let format: Intl.NumberFormat;
  try {
    format = new Intl.NumberFormat(locale, {
      style: "currency",
      currency,
      ...(whole ? { minimumFractionDigits: 0, maximumFractionDigits: 0 } : {}),
    });
  } catch {
    return `${amount} ${currency}`;
  }

  if (!rtlLocales.has(locale.split("-")[0])) return format.format(amount);

  const parts = format.formatToParts(amount);
  const number = parts
    .filter((p) => p.type !== "currency" && p.type !== "literal")
    .map((p) => p.value)
    .join("");
  const symbol = parts.find((p) => p.type === "currency")?.value ?? currency;
  return `${number} ${symbol}`;
}

/** A share (0.2) as a percentage in the reader's digits: "۲۰٪", "20%". */
export function formatPercent(share: number, locale: string): string {
  try {
    return new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: 0 }).format(share).replace(/‎/g, "");
  } catch {
    return `${Math.round(share * 100)}%`;
  }
}

/** A plain number (spec values like 6.5 or 2400) in the reader's digits and separators. */
export function formatNumber(value: number, locale: string, maximumFractionDigits = 2): string {
  try {
    return new Intl.NumberFormat(locale, { maximumFractionDigits }).format(value);
  } catch {
    return String(value);
  }
}

export type MoneyOptions = {
  /** Shop-defined symbol shown instead of the standard one ("تومان"). */
  symbol?: string | null;
  /** Digits after the decimal point for amounts that are not whole. */
  decimals?: number;
};

function isKnownCurrency(code: string): boolean {
  try {
    new Intl.NumberFormat("en", { style: "currency", currency: code });
    return /^[A-Z]{3}$/.test(code) && (Intl.supportedValuesOf("currency") as string[]).includes(code);
  } catch {
    return false;
  }
}

/** The discount share of an old price, or null when there is no real discount. */
export function discountShare(price: number, compareAtPrice: number | null | undefined): number | null {
  if (compareAtPrice == null || compareAtPrice <= price || compareAtPrice <= 0) return null;
  return (compareAtPrice - price) / compareAtPrice;
}

const rtlLocales = new Set(["fa", "ar"]);
