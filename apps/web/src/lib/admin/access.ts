import "server-only";
import { notFound, redirect } from "next/navigation";
import { getCurrentUser, type CurrentUser } from "@/lib/account/session";

/**
 * For admin pages: the signed-in user when they hold the permission. Signed-out (or expired) sessions go to sign-in
 * and come back; signed-in users without the permission get a plain 404, so the panel's pages are not revealed.
 * The API checks the permission again on every call — this only decides what to render.
 */
export async function requirePermission(locale: string, permission: string, returnPath: string): Promise<CurrentUser> {
  const user = await getCurrentUser();
  if (!user) redirect(`/${locale}/account/login?returnUrl=${encodeURIComponent(returnPath)}`);
  if (!user.permissions.includes(permission)) notFound();
  return user;
}

/** Staff see the admin panel's start page; customers do not. */
export async function requireStaff(locale: string, returnPath: string): Promise<CurrentUser> {
  const user = await getCurrentUser();
  if (!user) redirect(`/${locale}/account/login?returnUrl=${encodeURIComponent(returnPath)}`);
  if (user.permissions.length === 0) notFound();
  return user;
}
