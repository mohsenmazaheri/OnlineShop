import type { Metadata } from "next";
import { connection } from "next/server";
import { Suspense } from "react";
import { OrderDetails, OrderStatusBadge } from "@/components/orders/OrderDetails";
import { ReturnRequestForm } from "@/components/orders/ReturnRequestForm";
import { routeLocale } from "@/lib/account/page-helpers";
import { formatOrderNumber } from "@/lib/orders/format";
import { getDictionary } from "@/lib/i18n/dictionaries";
import { getOrderByLink } from "@/lib/orders/server";

// A guest order opened through the signed link of its emails: private, never indexed, the key never sent on.
export async function generateMetadata({ params }: PageProps<"/[locale]/order/[number]">): Promise<Metadata> {
  const dict = await getDictionary(routeLocale((await params).locale));
  return { title: dict.orders.guestTitle, robots: { index: false, follow: false }, referrer: "no-referrer" };
}

type Props = PageProps<"/[locale]/order/[number]">;

/** The number and key come from the address, so they are read inside Suspense. */
async function GuestOrder({ params, searchParams }: Props) {
  await connection();
  const { locale: raw, number: rawNumber } = await params;
  const locale = routeLocale(raw);
  const dict = await getDictionary(locale);
  const key = (await searchParams).key;
  const number = /^\d{1,15}$/.test(rawNumber) ? Number(rawNumber) : null;
  const order = number !== null && typeof key === "string" ? await getOrderByLink(number, key) : null;
  if (!order) return <p className="text-muted">{dict.orders.notFound}</p>;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-xl font-extrabold">{dict.orders.orderNumber.replace("{number}", formatOrderNumber(order.number, locale))}</h1>
        <OrderStatusBadge status={order.status} texts={dict.orders} />
      </div>
      <OrderDetails
        order={order}
        locale={locale}
        texts={dict.orders}
        invoiceHref={`/${locale}/order/${order.number}/invoice?key=${encodeURIComponent(key as string)}`}
        returnForm={
          order.actions.canRequestReturn ? (
            <ReturnRequestForm
              order={order}
              locale={locale}
              endpoint={`/api/v1/orders/${order.number}/returns?key=${encodeURIComponent(key as string)}`}
              texts={dict.orders}
              errors={dict.orders.errors}
            />
          ) : null
        }
      />
    </div>
  );
}

export default function GuestOrderPage({ params, searchParams }: Props) {
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 flex-col gap-4 px-4 py-8">
      <Suspense fallback={<p role="status">…</p>}>
        <GuestOrder params={params} searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
