import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { OrderList } from "@/components/orders/OrderList";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getAdminOrders, type OrderStatus } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/orders">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.ordersTitle);
}

const statuses: OrderStatus[] = ["Pending", "Paid", "Shipped", "Completed", "Canceled"];

type Search = PageProps<"/[locale]/admin/orders">["searchParams"];

/** Every order (orders.view), newest first, filtered by status and a search (a GET form: works without JavaScript). */
async function Orders({ locale, dict, searchParams }: { locale: Locale; dict: Dictionary; searchParams: Search }) {
  const user = await requirePermission(locale, "orders.view", `/${locale}/admin/orders`);
  const query = await searchParams;
  const status = statuses.find((s) => s === query.status) ?? null;
  const q = typeof query.q === "string" ? query.q.trim().slice(0, 100) : "";
  const page = Math.max(1, Number(query.page) || 1);
  const orders = (await getAdminOrders({ status, q, page })) ?? { items: [], total: 0, page: 1, pageSize: 20 };
  const texts = dict.admin;
  const params = (p: number) => {
    const search = new URLSearchParams();
    if (status) search.set("status", status);
    if (q) search.set("q", q);
    if (p > 1) search.set("page", String(p));
    const text = search.toString();
    return `/${locale}/admin/orders${text ? `?${text}` : ""}`;
  };

  return (
    <AdminShell locale={locale} texts={texts} current="orders" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.ordersIntro}</p>
      <AdminCard title={texts.ordersTitle} id="orders">
        <form method="get" className="mb-4 flex flex-wrap items-end gap-2">
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium">{texts.ordersStatus}</span>
            <select name="status" defaultValue={status ?? ""} className="h-11 rounded-xl border border-border bg-surface px-3">
              <option value="">{texts.ordersAllStatuses}</option>
              {statuses.map((s) => (
                <option key={s} value={s}>
                  {dict.orders.status[s]}
                </option>
              ))}
            </select>
          </label>
          <label className="flex min-w-48 flex-1 flex-col gap-1 text-sm">
            <span className="font-medium">{texts.ordersSearch}</span>
            <input name="q" defaultValue={q} maxLength={100} placeholder={texts.ordersSearchHint} className="h-11 rounded-xl border border-border bg-surface px-3" />
          </label>
          <button type="submit" className="h-11 rounded-xl border border-border px-4 text-sm font-medium hover:bg-surface-muted">
            {texts.ordersFilter}
          </button>
        </form>
        <OrderList page={orders} locale={locale} texts={dict.orders} hrefFor={(n) => `/${locale}/admin/orders/${n}`} pageHref={params} staff />
      </AdminCard>
    </AdminShell>
  );
}

export default async function OrdersAdminPage({ params, searchParams }: PageProps<"/[locale]/admin/orders">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Orders locale={locale} dict={dict} searchParams={searchParams} />
    </Suspense>
  );
}
