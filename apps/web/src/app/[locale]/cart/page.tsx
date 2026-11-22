import type { Metadata } from "next";
import { connection } from "next/server";
import { Suspense } from "react";
import { CartContents } from "@/components/cart/CartContents";
import { Skeleton } from "@/components/ui/Surface";
import { routeLocale } from "@/lib/account/page-helpers";
import { getCart } from "@/lib/cart/server";
import { loadDisplayCurrency } from "@/lib/currency/load";
import { currencyName } from "@/lib/i18n/display-names";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

// The cart is private to the visitor: never indexed (robots.txt disallows /*/cart as well).
export async function generateMetadata({ params }: PageProps<"/[locale]/cart">): Promise<Metadata> {
  const dict = await getDictionary(routeLocale((await params).locale));
  return { title: dict.cart.title, robots: { index: false, follow: false } };
}

/** The visitor's cart, read at request time (per visitor, never cached), with current prices and stock. */
async function Cart({ locale, dict }: { locale: Locale; dict: Dictionary }) {
  await connection();
  const [cart, store] = await Promise.all([getCart(locale), loadStore()]);
  if (!cart) return <p role="alert" className="text-danger">{dict.cart.unreachable}</p>;
  const { money } = await loadDisplayCurrency(locale, store.currency);
  const approximate = money.isBase ? null : dict.product.approximate.replace("{base}", currencyName(money.base, locale));
  return <CartContents cart={cart} locale={locale} money={money} approximate={approximate} texts={dict.cart} errors={dict.cart.errors} />;
}

export default async function CartPage({ params }: PageProps<"/[locale]/cart">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 flex-col gap-5 px-4 py-8">
      <h1 className="text-xl font-extrabold">{dict.cart.title}</h1>
      <Suspense fallback={<Skeleton className="h-48 w-full" />}>
        <Cart locale={locale} dict={dict} />
      </Suspense>
    </main>
  );
}
