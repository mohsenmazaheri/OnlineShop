import { NextResponse, type NextRequest } from "next/server";
import { safeReturnPath } from "@/lib/account/return-path";
import { currencyCookieName, currencyCookieValue } from "@/lib/currency/display";
import { isLocale } from "@/lib/i18n/config";
import { siteUrl } from "@/lib/seo/config";

/**
 * GET /{locale}/currency?code=USD&returnTo=/en/p/1001/x — remembers the visitor's display currency for this language
 * and goes back. A plain link (works without JavaScript); robots.txt keeps crawlers out. An unknown code is harmless:
 * the pages ignore currencies the shop does not offer.
 */
export async function GET(request: NextRequest, { params }: RouteContext<"/[locale]/currency">) {
  const { locale } = await params;
  if (!isLocale(locale)) return new NextResponse(null, { status: 404 });

  const search = request.nextUrl.searchParams;
  const code = (search.get("code") ?? "").toUpperCase();
  const target = new URL(safeReturnPath(search.get("returnTo"), locale), request.url);
  const response = NextResponse.redirect(target, 303);
  if (/^[A-Z]{3}$/.test(code)) {
    response.cookies.set(currencyCookieName, currencyCookieValue(locale, code), {
      path: "/",
      maxAge: 60 * 60 * 24 * 365,
      sameSite: "lax",
      httpOnly: true,
      secure: siteUrl().startsWith("https://"),
    });
  }
  return response;
}
