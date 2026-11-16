/**
 * A date (and optionally time) in the reader's language and calendar, from the runtime's built-in ICU data:
 * Persian dates for fa (1405/08/25), Gregorian elsewhere. Invalid input comes back unchanged.
 */
export function formatDate(iso: string, locale: string, withTime = false): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  try {
    return new Intl.DateTimeFormat(locale, withTime ? { dateStyle: "medium", timeStyle: "short" } : { dateStyle: "medium" }).format(date);
  } catch {
    return date.toISOString().slice(0, withTime ? 16 : 10).replace("T", " ");
  }
}
