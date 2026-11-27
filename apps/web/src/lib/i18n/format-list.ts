/** "A, B and C" style lists with the reader's separators (Persian/Arabic comma for fa/ar), from the built-in ICU data. */
export function formatList(items: readonly string[], locale: string): string {
  try {
    return new Intl.ListFormat(locale, { style: "short", type: "unit" }).format(items);
  } catch {
    return items.join(", ");
  }
}

/** The parts of an address or similar line, joined by plain commas (no "and"): "، " for right-to-left languages. */
export function joinParts(parts: readonly (string | null | undefined)[], locale: string): string {
  const rtl = ["fa", "ar"].includes(locale.split("-")[0]);
  return parts.filter((part): part is string => Boolean(part)).join(rtl ? "، " : ", ");
}
