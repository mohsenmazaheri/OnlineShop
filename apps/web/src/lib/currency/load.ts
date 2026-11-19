import "server-only";
import { cookies } from "next/headers";
import { getCurrencySettings } from "@/lib/api/storefront";
import { currencyCookieName, resolveDisplayCurrency, type CurrencySettings, type DisplayCurrency } from "./display";

/**
 * The display currency of this request (the visitor's cookie, else the language's default). Reads the cookie, so it
 * runs at request time inside a <Suspense> boundary. Without the API, prices stay in the base currency.
 */
export async function loadDisplayCurrency(locale: string, baseCode: string): Promise<{ money: DisplayCurrency; settings: CurrencySettings }> {
  const [jar, settings] = await Promise.all([
    cookies(),
    getCurrencySettings().catch((): CurrencySettings => ({ base: baseCode, currencies: [], localeDefaults: {} })),
  ]);
  return { money: resolveDisplayCurrency(settings, locale, jar.get(currencyCookieName)?.value), settings };
}
