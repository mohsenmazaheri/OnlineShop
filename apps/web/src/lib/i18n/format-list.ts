/** "A, B and C" style lists with the reader's separators (Persian/Arabic comma for fa/ar), from the built-in ICU data. */
export function formatList(items: readonly string[], locale: string): string {
  try {
    return new Intl.ListFormat(locale, { style: "short", type: "unit" }).format(items);
  } catch {
    return items.join(", ");
  }
}
