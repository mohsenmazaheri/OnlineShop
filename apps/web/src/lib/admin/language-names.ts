import { nativeLanguageName } from "@/lib/i18n/display-names";

/** Each shop language by its own name ("فارسی", "English"), for admin forms with one field set per language. */
export function languageNames(locales: readonly string[]): Record<string, string> {
  return Object.fromEntries(locales.map((l) => [l, nativeLanguageName(l)]));
}
