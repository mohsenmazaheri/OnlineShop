/**
 * Money in the reader's language, from the runtime's built-in ICU data (`Intl.NumberFormat`): Persian digits for fa,
 * the currency's symbol or code, no ".00" on whole amounts. Right-to-left languages get the amount first and the
 * currency after it ("۳۴۹٬۰۰۰٬۰۰۰ ریال"), as Persian and Arabic shops write prices.
 */
export function formatMoney(amount: number, currency: string, locale: string): string {
  const whole = Number.isInteger(amount);
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
export function formatNumber(value: number, locale: string): string {
  try {
    return new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(value);
  } catch {
    return String(value);
  }
}

/** The discount share of an old price, or null when there is no real discount. */
export function discountShare(price: number, compareAtPrice: number | null | undefined): number | null {
  if (compareAtPrice == null || compareAtPrice <= price || compareAtPrice <= 0) return null;
  return (compareAtPrice - price) / compareAtPrice;
}

const rtlLocales = new Set(["fa", "ar"]);
