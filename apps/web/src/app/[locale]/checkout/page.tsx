import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { CheckoutForm } from "@/components/checkout/CheckoutForm";
import { Skeleton } from "@/components/ui/Surface";
import { routeLocale } from "@/lib/account/page-helpers";
import { getCurrencySettings } from "@/lib/api/storefront";
import { getCheckout } from "@/lib/checkout/server";
import { baseCurrency, type CurrencySettings } from "@/lib/currency/display";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

// Checkout is private to the visitor: never indexed (robots.txt disallows it as well).
export async function generateMetadata({ params }: PageProps<"/[locale]/checkout">): Promise<Metadata> {
  const dict = await getDictionary(routeLocale((await params).locale));
  return { title: dict.checkout.title, robots: { index: false, follow: false } };
}

/** Read per request (the visitor's cart and account); an empty cart goes back to the cart page. */
async function Checkout({ locale, dict }: { locale: Locale; dict: Dictionary }) {
  await connection();
  const [view, store] = await Promise.all([getCheckout(locale), loadStore()]);
  if (!view) return <p role="alert" className="text-danger">{dict.checkout.unreachable}</p>;
  if (view.cart.lines.length === 0) redirect(`/${locale}/cart`);
  const signInHref = `/${locale}/account/login?returnUrl=${encodeURIComponent(`/${locale}/checkout`)}`;
  if (!view.signedIn && !view.guestCheckout) redirect(signInHref);
  const settings = await getCurrencySettings().catch((): CurrencySettings => ({ base: store.currency, currencies: [], localeDefaults: {} }));
  return (
    <CheckoutForm
      locale={locale}
      view={view}
      money={baseCurrency(settings)}
      texts={dict.checkout}
      addressTexts={dict.account}
      errors={{ ...dict.account.errors, ...dict.checkout.errors }}
      signInHref={signInHref}
    />
  );
}

export default async function CheckoutPage({ params }: PageProps<"/[locale]/checkout">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 flex-col gap-5 px-4 py-8">
      <h1 className="text-xl font-extrabold">{dict.checkout.title}</h1>
      <Suspense fallback={<Skeleton className="h-96 w-full" />}>
        <Checkout locale={locale} dict={dict} />
      </Suspense>
    </main>
  );
}
