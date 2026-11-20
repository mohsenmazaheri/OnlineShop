import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { StocktakeActions } from "@/components/admin/StocktakeActions";
import { StocktakeCountForm } from "@/components/admin/StocktakeCountForm";
import { StocktakeLines } from "@/components/admin/StocktakeLines";
import { statusTones } from "@/components/admin/StocktakeList";
import { Badge } from "@/components/ui/Surface";
import { requirePermission } from "@/lib/admin/access";
import { parseStocktakeFilter, stocktakeLabel } from "@/lib/admin/inventory";
import { accountMetadata, firstParam, routeLocale } from "@/lib/account/page-helpers";
import { getStocktake } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatNumber } from "@/lib/i18n/format-money";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/stocktakes/[number]">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.stocktakesTitle);
}

type Params = PageProps<"/[locale]/admin/stocktakes/[number]">["params"];
type Search = Promise<Record<string, string | string[] | undefined>>;

/** One stocktake: totals, count entry and closing while open, and its lines with filters. */
async function Stocktake({ params, searchParams }: { params: Params; searchParams: Search }) {
  const { locale: raw, number: rawNumber } = await params;
  const locale: Locale = routeLocale(raw);
  const number = Number.parseInt(rawNumber, 10);
  if (!Number.isInteger(number) || number <= 0 || String(number) !== rawNumber) notFound();
  const texts: Dictionary["admin"] = (await getDictionary(locale)).admin;
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/stocktakes/${number}`);
  const query = await searchParams;
  const filter = parseStocktakeFilter(firstParam(query.filter));
  const search = firstParam(query.search).slice(0, 100);
  const page = Math.max(1, Number.parseInt(firstParam(query.page), 10) || 1);
  const stocktake = await getStocktake(number, { filter, search, page, locale });
  if (!stocktake) notFound();
  const editable = stocktake.status === "Open" && user.permissions.includes("inventory.manage");
  const totals = stocktake.totals;
  const n = (value: number) => formatNumber(value, locale);

  return (
    <AdminShell locale={locale} texts={texts} current="stocktakes" permissions={user.permissions}>
      <Link href={`/${locale}/admin/stocktakes`} className="text-sm text-primary underline-offset-2 hover:underline">
        {texts.stocktakeBack}
      </Link>
      <AdminCard title={texts.stocktakeHeading.replace("{number}", stocktakeLabel(stocktake.number))} id="summary">
        <div className="flex flex-col gap-2 text-sm">
          <p className="flex flex-wrap items-center gap-2">
            <span className="font-medium">{stocktake.warehouse}</span>
            <Badge tone={statusTones[stocktake.status]}>{texts.stocktakeStatuses[stocktake.status]}</Badge>
          </p>
          <p className="text-muted">
            {texts.stocktakeOpened.replace("{date}", formatDate(stocktake.createdUtc, locale, true))}
            {stocktake.openedBy && ` ${texts.stocktakeBy.replace("{name}", stocktake.openedBy)}`}
            {stocktake.closedUtc && ` · ${texts.stocktakeClosedAt.replace("{date}", formatDate(stocktake.closedUtc, locale, true))}`}
            {stocktake.closedBy && ` ${texts.stocktakeBy.replace("{name}", stocktake.closedBy)}`}
          </p>
          {stocktake.note && <p>{stocktake.note}</p>}
          <p className="font-medium">
            {texts.stocktakeTotals
              .replace("{counted}", n(totals.counted))
              .replace("{lines}", n(totals.lines))
              .replace("{differences}", n(totals.withDifference))
              .replace("{over}", n(totals.unitsOver))
              .replace("{short}", n(totals.unitsShort))}
          </p>
        </div>
      </AdminCard>
      {editable && (
        <>
          <AdminCard title={texts.stocktakeCountTitle} id="count">
            <StocktakeCountForm locale={locale} texts={texts} number={stocktake.number} />
          </AdminCard>
          <AdminCard title={texts.stocktakeFilesTitle} id="actions">
            <StocktakeActions locale={locale} texts={texts} number={stocktake.number} />
          </AdminCard>
        </>
      )}
      <AdminCard title={texts.stocktakeLinesTitle} id="lines">
        <StocktakeLines locale={locale} texts={texts} stocktake={stocktake} filter={filter} search={search} />
      </AdminCard>
    </AdminShell>
  );
}

/** The number is only known per request: everything, the texts included, loads inside Suspense. */
export default function StocktakePage({ params, searchParams }: PageProps<"/[locale]/admin/stocktakes/[number]">) {
  return (
    <Suspense fallback={<p role="status" className="p-6">…</p>}>
      <Stocktake params={params} searchParams={searchParams} />
    </Suspense>
  );
}
