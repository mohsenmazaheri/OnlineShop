/** The comparison list's rules, without React or storage (the store and the components use these). */

export const maxCompare = 4;

/** A product on the list: its public number and category slug ("" when not known, e.g. loaded from the account). */
export type CompareEntry = { n: number; c: string };

export type AddOutcome =
  | { kind: "added"; list: CompareEntry[] }
  | { kind: "present" }
  | { kind: "full" }
  | { kind: "otherCategory" };

/** Adds a product unless it is there already, the list is full, or it is from another category than the list. */
export function addEntry(list: readonly CompareEntry[], entry: CompareEntry): AddOutcome {
  if (list.some((e) => e.n === entry.n)) return { kind: "present" };
  if (list.length >= maxCompare) return { kind: "full" };
  const known = list.map((e) => e.c).filter((c) => c.length > 0);
  if (entry.c && known.length > 0 && known.some((c) => c !== entry.c)) return { kind: "otherCategory" };
  return { kind: "added", list: [...list, entry] };
}

export function removeEntry(list: readonly CompareEntry[], number: number): CompareEntry[] {
  return list.filter((e) => e.n !== number);
}

/**
 * After signing in: the account's list first, then the products added as a guest, without repeats, at most four.
 * Categories known from the guest list are kept.
 */
export function mergeLists(account: readonly number[], guest: readonly CompareEntry[]): CompareEntry[] {
  const merged: CompareEntry[] = [];
  for (const n of [...account, ...guest.map((e) => e.n)]) {
    if (merged.length >= maxCompare || merged.some((e) => e.n === n)) continue;
    merged.push({ n, c: guest.find((e) => e.n === n)?.c ?? "" });
  }
  return merged;
}

/** What the browser keeps: the list, and whether it belongs to a signed-in account (cleared once signed out). */
export type StoredCompare = { list: CompareEntry[]; account: boolean };

export function parseStored(text: string | null): StoredCompare {
  try {
    const value = JSON.parse(text ?? "") as Partial<StoredCompare>;
    const list = Array.isArray(value.list)
      ? value.list
          .filter((e): e is CompareEntry => Number.isSafeInteger(e?.n) && e.n > 0 && typeof e.c === "string")
          .slice(0, maxCompare)
      : [];
    return { list, account: value.account === true };
  } catch {
    return { list: [], account: false };
  }
}

/** "12,15" → [12, 15] (repeats and anything else dropped, at most four), as the comparison page reads ?ids=. */
export function parseIds(value: string | string[] | undefined): number[] {
  const text = Array.isArray(value) ? value.join(",") : (value ?? "");
  const ids: number[] = [];
  for (const part of text.split(/[,\s]+/)) {
    const n = /^\d{1,15}$/.test(part) ? Number(part) : 0;
    if (n > 0 && !ids.includes(n) && ids.length < maxCompare) ids.push(n);
  }
  return ids;
}

/** The comparison page's address (shareable); "only differences" as ?diff=1. */
export function comparePath(locale: string, numbers: readonly number[], onlyDifferences = false): string {
  const params = new URLSearchParams();
  if (numbers.length > 0) params.set("ids", numbers.join(","));
  if (onlyDifferences) params.set("diff", "1");
  const query = params.toString().replaceAll("%2C", ",");
  return query ? `/${locale}/compare?${query}` : `/${locale}/compare`;
}
