import { redirect } from "next/navigation";
import { Suspense } from "react";
import { AccountNav } from "@/components/account/AccountNav";
import { ProductCard } from "@/components/product/ProductCard";
import { WishlistRemove } from "@/components/wishlist/WishlistRemove";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getWishlist } from "@/lib/account/session";
import { loadDisplayCurrency } from "@/lib/currency/load";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

export async function generateMetadata({ params }: PageProps<"/[locale]/account/wishlist">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.account.wishlistTitle);
}

/** The wishlist is per user: read at request time, never cached. Prices and stock are the current ones. */
async function Wishlist({ locale, dict }: { locale: Locale; dict: Dictionary }) {
  const [products, store] = await Promise.all([getWishlist(locale), loadStore()]);
  // proxy.ts already redirects visitors without a session cookie; this covers expired sessions.
  if (!products) redirect(`/${locale}/account/login?returnUrl=${encodeURIComponent(`/${locale}/account/wishlist`)}`);
  if (products.length === 0) return <p className="text-muted">{dict.account.wishlistEmpty}</p>;
  const { money } = await loadDisplayCurrency(locale, store.currency);
  return (
    <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
      {products.map((product) => (
        <li key={product.number} className="flex flex-col gap-2">
          <ProductCard product={product} locale={locale} money={money} texts={dict.product} />
          <WishlistRemove number={product.number} label={dict.account.wishlistRemove.replace("{name}", product.name)} />
        </li>
      ))}
    </ul>
  );
}

export default async function WishlistPage({ params }: PageProps<"/[locale]/account/wishlist">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 flex-col px-4 py-8">
      <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
        <h1 className="mb-5 text-xl font-bold">{dict.account.wishlistTitle}</h1>
        <div className="max-w-md">
          <AccountNav locale={locale} texts={dict.account} current="wishlist" />
        </div>
        <Suspense fallback={<p role="status">{dict.account.working}</p>}>
          <Wishlist locale={locale} dict={dict} />
        </Suspense>
      </section>
    </main>
  );
}
