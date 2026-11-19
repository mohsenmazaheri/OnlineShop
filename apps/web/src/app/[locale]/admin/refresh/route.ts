import { revalidateTag } from "next/cache";
import { getCurrentUser } from "@/lib/account/session";
import { allowedTags } from "@/lib/admin/refresh";
import { siteUrl } from "@/lib/seo/config";

/**
 * POST /{locale}/admin/refresh {"tags":["store"]} — clears the storefront's cached store settings or currencies
 * right after an admin saves them. Only from the shop's own pages (Origin check, like the API's CSRF rule) and only
 * for a signed-in user with the matching permission.
 */
export async function POST(request: Request): Promise<Response> {
  if (request.headers.get("origin") !== new URL(siteUrl()).origin) return Response.json({ error: "forbidden" }, { status: 403 });

  const user = await getCurrentUser();
  if (!user) return Response.json({ error: "unauthorized" }, { status: 401 });

  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return Response.json({ error: "invalid_body" }, { status: 400 });
  }

  const tags = allowedTags(body, user.permissions);
  if (tags.length === 0) return Response.json({ error: "forbidden" }, { status: 403 });
  // Expire at once: the admin wants to see the change on the next page load.
  for (const tag of tags) revalidateTag(tag, { expire: 0 });
  return Response.json({ refreshed: tags });
}
