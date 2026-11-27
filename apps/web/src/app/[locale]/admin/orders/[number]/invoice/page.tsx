import Link from "next/link";
import { Suspense } from "react";
import { InvoiceDocument } from "@/components/orders/InvoiceDocument";
import { PrintButton } from "@/components/orders/PrintButton";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getAdminInvoice } from "@/lib/account/session";
import { getDictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/orders/[number]/invoice">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.orders.invoiceTitle);
}

type Params = PageProps<"/[locale]/admin/orders/[number]/invoice">["params"];

/** An order's invoice for staff (orders.view), outside the admin frame so it prints alone. */
async function Invoice({ params }: { params: Params }) {
  const { locale: raw, number: rawNumber } = await params;
  const locale = routeLocale(raw);
  await requirePermission(locale, "orders.view", `/${locale}/admin/orders/${rawNumber}/invoice`);
  const number = /^\d{1,15}$/.test(rawNumber) ? Number(rawNumber) : null;
  const [dict, invoice, store] = await Promise.all([getDictionary(locale), number === null ? null : getAdminInvoice(number), loadStore()]);
  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3 print:hidden">
        <Link href={`/${locale}/admin/orders/${rawNumber}`} className="text-sm text-primary hover:underline">
          {dict.orders.backToOrder}
        </Link>
        {invoice && <PrintButton label={dict.orders.invoicePrint} />}
      </div>
      {invoice ? <InvoiceDocument invoice={invoice} locale={locale} shopName={store.name} texts={dict.orders} /> : <p className="text-muted">{dict.orders.noInvoice}</p>}
    </>
  );
}

export default function AdminInvoicePage({ params }: PageProps<"/[locale]/admin/orders/[number]/invoice">) {
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-1 flex-col gap-4 px-4 py-8 print:p-0">
      <Suspense fallback={<p role="status">…</p>}>
        <Invoice params={params} />
      </Suspense>
    </main>
  );
}
