/** Storefront caches an admin page may clear after a save, and the permission each one needs. */
export const refreshableTags = { store: "settings.manage", currencies: "settings.manage", catalog: "catalog.manage", reviews: "reviews.moderate" } as const;

export type RefreshableTag = keyof typeof refreshableTags;

/** The tags of a request body that the user may clear (unknown tags and missing permissions are dropped). */
export function allowedTags(body: unknown, permissions: readonly string[]): RefreshableTag[] {
  const tags = (body as { tags?: unknown } | null)?.tags;
  if (!Array.isArray(tags)) return [];
  return [...new Set(tags)].filter(
    (t): t is RefreshableTag => typeof t === "string" && t in refreshableTags && permissions.includes(refreshableTags[t as RefreshableTag]),
  );
}

/**
 * Asks the web app to drop its cached copy so the storefront shows an admin change at once (instead of after the
 * cache's lifetime). Called from the browser after a successful save; failures only delay the update.
 */
export async function refreshStorefront(locale: string, tags: RefreshableTag[], fetchImpl: typeof fetch = fetch): Promise<void> {
  try {
    await fetchImpl(`/${locale}/admin/refresh`, {
      method: "POST",
      credentials: "same-origin",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ tags }),
    });
  } catch {
    // The cache expires on its own.
  }
}
