import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { OpenStocktakeForm } from "@/components/admin/OpenStocktakeForm";
import { StocktakeList } from "@/components/admin/StocktakeList";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getStocktakes, getWarehouses } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/stocktakes">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.stocktakesTitle);
}

/** Every stocktake (inventory.view); starting one needs inventory.manage. */
async function Stocktakes({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/stocktakes`);
  const [stocktakes, warehouses] = await Promise.all([getStocktakes(), getWarehouses()]);

  return (
    <AdminShell locale={locale} texts={texts} current="stocktakes" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.stocktakesIntro}</p>
      {user.permissions.includes("inventory.manage") && (
        <AdminCard title={texts.stocktakeStart} id="start">
          <OpenStocktakeForm locale={locale} texts={texts} warehouses={warehouses ?? []} />
        </AdminCard>
      )}
      <AdminCard title={texts.stocktakesTitle} id="stocktakes">
        <StocktakeList locale={locale} texts={texts} stocktakes={stocktakes ?? []} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function StocktakesPage({ params }: PageProps<"/[locale]/admin/stocktakes">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Stocktakes locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
