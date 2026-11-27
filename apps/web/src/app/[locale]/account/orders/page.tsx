import { redirect } from "next/navigation";
import { Suspense } from "react";
import { AccountNav } from "@/components/account/AccountNav";
import { OrderList } from "@/components/orders/OrderList";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getMyOrders } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/account/orders">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.orders.title);
}

type Search = PageProps<"/[locale]/account/orders">["searchParams"];

/** The customer's orders: read at request time, never cached. */
async function Orders({ locale, dict, searchParams }: { locale: Locale; dict: Dictionary; searchParams: Search }) {
  const page = Math.max(1, Number((await searchParams).page) || 1);
  const orders = await getMyOrders(page);
  // proxy.ts already redirects visitors without a session cookie; this covers expired sessions.
  if (!orders) redirect(`/${locale}/account/login?returnUrl=${encodeURIComponent(`/${locale}/account/orders`)}`);
  return (
    <OrderList
      page={orders}
      locale={locale}
      texts={dict.orders}
      hrefFor={(n) => `/${locale}/account/orders/${n}`}
      pageHref={(p) => `/${locale}/account/orders${p > 1 ? `?page=${p}` : ""}`}
    />
  );
}

export default async function OrdersPage({ params, searchParams }: PageProps<"/[locale]/account/orders">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 flex-col px-4 py-8">
      <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
        <h1 className="mb-5 text-xl font-bold">{dict.orders.title}</h1>
        <div className="max-w-xl">
          <AccountNav locale={locale} texts={dict.account} current="orders" />
        </div>
        <Suspense fallback={<p role="status">{dict.account.working}</p>}>
          <Orders locale={locale} dict={dict} searchParams={searchParams} />
        </Suspense>
      </section>
    </main>
  );
}
