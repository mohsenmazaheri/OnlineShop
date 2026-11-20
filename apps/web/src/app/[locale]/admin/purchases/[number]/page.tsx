import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ReceiptLines, receiptTones, receiptTotals } from "@/components/admin/PurchaseList";
import { ReceiptEditor } from "@/components/admin/ReceiptEditor";
import { Badge } from "@/components/ui/Surface";
import { requirePermission } from "@/lib/admin/access";
import { purchaseLabel } from "@/lib/admin/inventory";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getPurchase, getSuppliers, getWarehouses } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/purchases/[number]">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.purchasesTitle);
}

type Params = PageProps<"/[locale]/admin/purchases/[number]">["params"];

/** One purchase receipt: editable while a draft (inventory.manage), otherwise its lines read-only. */
async function Purchase({ params }: { params: Params }) {
  const { locale: raw, number: rawNumber } = await params;
  const locale: Locale = routeLocale(raw);
  const number = Number.parseInt(rawNumber, 10);
  if (!Number.isInteger(number) || number <= 0 || String(number) !== rawNumber) notFound();
  const texts: Dictionary["admin"] = (await getDictionary(locale)).admin;
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/purchases/${number}`);
  const [receipt, suppliers, warehouses] = await Promise.all([getPurchase(number, locale), getSuppliers(), getWarehouses()]);
  if (!receipt) notFound();
  const { summary } = receipt;
  const editable = summary.status === "Draft" && user.permissions.includes("inventory.manage");

  return (
    <AdminShell locale={locale} texts={texts} current="purchases" permissions={user.permissions}>
      <Link href={`/${locale}/admin/purchases`} className="text-sm text-primary underline-offset-2 hover:underline">
        {texts.purchaseBack}
      </Link>
      <AdminCard title={texts.purchaseHeading.replace("{number}", purchaseLabel(summary.number))} id="summary">
        <div className="flex flex-col gap-2 text-sm">
          <p className="flex flex-wrap items-center gap-2">
            <span className="font-medium">{summary.supplier}</span>
            <Badge tone={receiptTones[summary.status]}>{texts.purchaseStatuses[summary.status]}</Badge>
            <span className="text-muted">{summary.warehouse}</span>
          </p>
          <p className="text-muted">
            {texts.stocktakeOpened.replace("{date}", formatDate(summary.createdUtc, locale, true))}
            {receipt.createdBy && ` ${texts.stocktakeBy.replace("{name}", receipt.createdBy)}`}
            {summary.closedUtc && ` · ${texts.stocktakeClosedAt.replace("{date}", formatDate(summary.closedUtc, locale, true))}`}
            {receipt.closedBy && ` ${texts.stocktakeBy.replace("{name}", receipt.closedBy)}`}
          </p>
          <p className="font-medium">{receiptTotals(summary, texts, locale)}</p>
        </div>
      </AdminCard>
      <AdminCard title={texts.purchaseLines} id="lines">
        {editable ? (
          <ReceiptEditor
            locale={locale}
            texts={texts}
            currency={summary.currency}
            suppliers={suppliers ?? []}
            warehouses={warehouses ?? []}
            receipt={receipt}
          />
        ) : (
          <>
            {summary.supplierReference && (
              <p className="text-sm" dir="ltr">
                {summary.supplierReference}
              </p>
            )}
            {receipt.note && <p className="text-sm text-muted">{receipt.note}</p>}
            <ReceiptLines locale={locale} texts={texts} receipt={receipt} />
          </>
        )}
      </AdminCard>
    </AdminShell>
  );
}

/** The number is only known per request: everything, the texts included, loads inside Suspense. */
export default function PurchasePage({ params }: PageProps<"/[locale]/admin/purchases/[number]">) {
  return (
    <Suspense fallback={<p role="status" className="p-6">…</p>}>
      <Purchase params={params} />
    </Suspense>
  );
}
