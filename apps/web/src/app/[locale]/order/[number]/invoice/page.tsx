import type { Metadata } from "next";
import Link from "next/link";
import { connection } from "next/server";
import { Suspense } from "react";
import { InvoiceDocument } from "@/components/orders/InvoiceDocument";
import { PrintButton } from "@/components/orders/PrintButton";
import { routeLocale } from "@/lib/account/page-helpers";
import { getDictionary } from "@/lib/i18n/dictionaries";
import { getInvoiceByLink } from "@/lib/orders/server";
import { loadStore } from "@/lib/store/load";

// A guest's invoice through the order's signed link: private, never indexed, the key never sent on.
export async function generateMetadata({ params }: PageProps<"/[locale]/order/[number]/invoice">): Promise<Metadata> {
  const dict = await getDictionary(routeLocale((await params).locale));
  return { title: dict.orders.invoiceTitle, robots: { index: false, follow: false }, referrer: "no-referrer" };
}

type Props = PageProps<"/[locale]/order/[number]/invoice">;

async function GuestInvoice({ params, searchParams }: Props) {
  await connection();
  const { locale: raw, number: rawNumber } = await params;
  const locale = routeLocale(raw);
  const key = (await searchParams).key;
  const number = /^\d{1,15}$/.test(rawNumber) ? Number(rawNumber) : null;
  const [dict, store] = await Promise.all([getDictionary(locale), loadStore()]);
  const invoice = number !== null && typeof key === "string" ? await getInvoiceByLink(number, key) : null;
  return (
    <>
      {number !== null && typeof key === "string" && (
        <div className="flex flex-wrap items-center justify-between gap-3 print:hidden">
          <Link href={`/${locale}/order/${number}?key=${encodeURIComponent(key)}`} className="text-sm text-primary hover:underline">
            {dict.orders.backToOrder}
          </Link>
          {invoice && <PrintButton label={dict.orders.invoicePrint} />}
        </div>
      )}
      {invoice ? <InvoiceDocument invoice={invoice} locale={locale} shopName={store.name} texts={dict.orders} /> : <p className="text-muted">{dict.orders.noInvoice}</p>}
    </>
  );
}

export default function GuestInvoicePage({ params, searchParams }: Props) {
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-1 flex-col gap-4 px-4 py-8 print:p-0">
      <Suspense fallback={<p role="status">…</p>}>
        <GuestInvoice params={params} searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
