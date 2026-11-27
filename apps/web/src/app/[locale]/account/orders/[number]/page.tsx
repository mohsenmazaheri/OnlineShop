import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { Suspense } from "react";
import { OrderDetails, OrderStatusBadge } from "@/components/orders/OrderDetails";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getCurrentUser, getMyOrder } from "@/lib/account/session";
import { formatOrderNumber } from "@/lib/orders/format";
import { getDictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/account/orders/[number]">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.orders.title);
}

type Params = PageProps<"/[locale]/account/orders/[number]">["params"];

/** One of the customer's own orders (another customer's number is "not found"); the number is read inside Suspense. */
async function Order({ params }: { params: Params }) {
  const { locale: raw, number: rawNumber } = await params;
  const locale = routeLocale(raw);
  const dict = await getDictionary(locale);
  const number = /^\d{1,15}$/.test(rawNumber) ? Number(rawNumber) : null;
  if (number === null) notFound();
  const order = await getMyOrder(number);
  if (!order && !(await getCurrentUser())) redirect(`/${locale}/account/login?returnUrl=${encodeURIComponent(`/${locale}/account/orders/${number}`)}`);
  return (
    <>
      <Link href={`/${locale}/account/orders`} className="text-sm text-primary hover:underline">
        {dict.orders.backToOrders}
      </Link>
      {order ? (
        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-xl font-extrabold">{dict.orders.orderNumber.replace("{number}", formatOrderNumber(order.number, locale))}</h1>
            <OrderStatusBadge status={order.status} texts={dict.orders} />
          </div>
          <OrderDetails order={order} locale={locale} texts={dict.orders} />
        </div>
      ) : (
        <p className="text-muted">{dict.orders.notFound}</p>
      )}
    </>
  );
}

export default function OrderPage({ params }: PageProps<"/[locale]/account/orders/[number]">) {
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 flex-col gap-4 px-4 py-8">
      <Suspense fallback={<p role="status">…</p>}>
        <Order params={params} />
      </Suspense>
    </main>
  );
}
