import type { components } from "@/lib/api/schema";

export type RedirectRule = components["schemas"]["RedirectRule"];

const successTtlMs = 60_000;
const failureTtlMs = 5_000;
const timeoutMs = 2_000;

/** Old address → rule, keyed like the API stores them: decoded, without trailing slash, with the query if any. */
export type RedirectTable = Map<string, RedirectRule>;

/**
 * The admin's redirects for proxy.ts, kept in memory for a minute (a change shows up within a minute); a failed load
 * means "no redirects" for a few seconds, so the shop keeps working while the API restarts.
 */
export function createRedirectCache(apiBaseUrl: () => string, fetchImpl: typeof fetch = fetch, now: () => number = Date.now) {
  let entry: { value: RedirectTable; expires: number } | null = null;
  let pending: Promise<RedirectTable> | null = null;

  async function load(): Promise<RedirectTable> {
    try {
      const response = await fetchImpl(`${apiBaseUrl()}/api/v1/redirects`, { headers: { Accept: "application/json" }, signal: AbortSignal.timeout(timeoutMs) });
      if (!response.ok) throw new Error(`status ${response.status}`);
      const rules = (await response.json()) as RedirectRule[];
      const value: RedirectTable = new Map(rules.map((r) => [r.fromPath, r]));
      entry = { value, expires: now() + successTtlMs };
      return value;
    } catch {
      entry = { value: new Map(), expires: now() + failureTtlMs };
      return entry.value;
    }
  }

  return async function get(): Promise<RedirectTable> {
    if (entry && entry.expires > now()) return entry.value;
    pending ??= load().finally(() => {
      pending = null;
    });
    return pending;
  };
}

function decode(text: string): string | null {
  try {
    return decodeURIComponent(text);
  } catch {
    return null;
  }
}

/** The rule for a request: the exact path with its query first, then the path alone (trailing slash ignored). */
export function matchRedirect(table: RedirectTable, pathname: string, search: string): RedirectRule | undefined {
  if (table.size === 0) return undefined;
  const raw = decode(pathname);
  if (raw === null) return undefined;
  const path = raw.length > 1 ? raw.replace(/\/+$/, "") : raw;
  const query = search && search !== "?" ? decode(search) : null;
  return (query ? table.get(path + query) : undefined) ?? table.get(path);
}

/** Status and target of a matched rule (410 has no target). Site paths are resolved against the request. */
export function redirectAnswer(rule: RedirectRule, requestUrl: string): { status: 301 | 302 | 410; location: string | null } {
  if (rule.kind === "Gone" || !rule.toPath) return { status: 410, location: null };
  return { status: rule.kind === "Temporary" ? 302 : 301, location: new URL(rule.toPath, requestUrl).toString() };
}
