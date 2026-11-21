/** Special offers with an end time: cache lifetimes, the countdown, and the editor's date field. */

type Profile = "minutes" | "hours";

/** Seconds of the built-in profiles' revalidate time (see Next.js cacheLife). */
const revalidateSeconds: Record<Profile, number> = { minutes: 60, hours: 3600 };

/** The API ends offers once a minute: a cache expires this long after the end, when the price is surely back. */
export const expiryMarginSeconds = 90;

/**
 * The cache lifetime for data that holds offer end times: the usual profile, unless an offer ends sooner, then a
 * lifetime that runs out just after it (so a page never shows an offer price once the offer is over).
 */
export function saleAwareLife(ends: readonly (string | null | undefined)[], profile: Profile, now = Date.now()): Profile | { stale: number; revalidate: number; expire: number } {
  const soonest = ends
    .map((e) => (e ? Date.parse(e) : Number.NaN))
    .filter((t) => Number.isFinite(t) && t > now - expiryMarginSeconds * 1000)
    .reduce((min, t) => Math.min(min, t), Number.POSITIVE_INFINITY);
  if (!Number.isFinite(soonest)) return profile;
  const seconds = Math.max(30, Math.ceil((soonest - now) / 1000) + expiryMarginSeconds);
  if (seconds >= revalidateSeconds[profile]) return profile;
  return { stale: Math.min(30, seconds), revalidate: seconds, expire: seconds + 60 };
}

/** What is left of an offer: whole days and the rest as hours, minutes, seconds (null once it has ended). */
export function remaining(endsUtc: string, now = Date.now()): { days: number; hours: number; minutes: number; seconds: number } | null {
  const left = Math.floor((Date.parse(endsUtc) - now) / 1000);
  if (!Number.isFinite(left) || left <= 0) return null;
  return { days: Math.floor(left / 86_400), hours: Math.floor((left % 86_400) / 3600), minutes: Math.floor((left % 3600) / 60), seconds: left % 60 };
}

/** An ISO time as the value of a datetime-local field, in the browser's time zone ("2026-11-30T18:00"). */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** A datetime-local value (browser time zone) as an ISO time for the API, or null when empty or invalid. */
export function fromLocalInput(value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}
