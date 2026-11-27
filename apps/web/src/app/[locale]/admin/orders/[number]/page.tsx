import Link from "next/link";
import { Suspense } from "react";
import { AdminShell } from "@/components/admin/AdminShell";
import { OrderActionsPanel } from "@/components/admin/OrderActionsPanel";
import { OrderReturnsPanel } from "@/components/admin/OrderReturnsPanel";
import { OrderDetails, OrderStatusBadge } from "@/components/orders/OrderDetails";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getAdminOrder } from "@/lib/account/session";
import { formatOrderNumber } from "@/lib/orders/format";
import { getDictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/orders/[number]">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.ordersTitle);
}

type Params = PageProps<"/[locale]/admin/orders/[number]">["params"];

/** One order for staff (orders.view; orders.manage to change it); the number is read inside Suspense. */
async function Order({ params }: { params: Params }) {
  const { locale: raw, number: rawNumber } = await params;
  const locale = routeLocale(raw);
  const dict = await getDictionary(locale);
  const user = await requirePermission(locale, "orders.view", `/${locale}/admin/orders/${rawNumber}`);
  const number = /^\d{1,15}$/.test(rawNumber) ? Number(rawNumber) : null;
  const order = number === null ? null : await getAdminOrder(number);
  const texts = dict.admin;
  return (
    <AdminShell locale={locale} texts={texts} current="orders" permissions={user.permissions}>
      <Link href={`/${locale}/admin/orders`} className="text-sm text-primary hover:underline">
        {texts.ordersBack}
      </Link>
      {!order ? (
        <p className="text-muted">{dict.orders.notFound}</p>
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-3">
            <h2 className="text-lg font-extrabold">{dict.orders.orderNumber.replace("{number}", formatOrderNumber(order.number, locale))}</h2>
            <OrderStatusBadge status={order.status} texts={dict.orders} />
            <span className="text-xs text-muted">{order.customerId ? texts.orderCustomer : texts.orderGuest}</span>
          </div>
          <OrderActionsPanel order={order} texts={texts} errors={dict.orders.errors} canManage={user.permissions.includes("orders.manage")} />
          <OrderReturnsPanel order={order} texts={texts} errors={dict.orders.errors} canManage={user.permissions.includes("orders.manage")} />
          <OrderDetails order={order} locale={locale} texts={dict.orders} staff invoiceHref={`/${locale}/admin/orders/${order.number}/invoice`} />
        </>
      )}
    </AdminShell>
  );
}

export default function OrderAdminPage({ params }: PageProps<"/[locale]/admin/orders/[number]">) {
  return (
    <Suspense fallback={<p role="status" className="p-6">…</p>}>
      <Order params={params} />
    </Suspense>
  );
}
