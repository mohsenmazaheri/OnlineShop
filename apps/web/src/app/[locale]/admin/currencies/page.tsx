import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { AddCurrencyForm, CurrencyForm, LocaleDefaultsForm, SyncRatesPanel } from "@/components/admin/Currencies";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getCurrencyAdmin } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { nativeLanguageName } from "@/lib/i18n/display-names";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/currencies">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.currenciesTitle);
}

/** Display currencies, their rates and each language's starting currency (permission settings.manage). */
async function Currencies({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "settings.manage", `/${locale}/admin/currencies`);
  const overview = await getCurrencyAdmin();
  const base = overview?.base ?? "";
  const currencies = overview?.currencies ?? [];
  const languageNames = Object.fromEntries((overview?.localeDefaults ?? []).map((d) => [d.locale, nativeLanguageName(d.locale)]));

  return (
    <AdminShell locale={locale} texts={texts} current="currencies" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.currenciesIntro.replace("{base}", base)}</p>
      <div className="grid gap-4 lg:grid-cols-3">
        <div className="flex flex-col gap-4 lg:col-span-2">
          <AdminCard title={texts.currenciesTitle} id="currencies-list">
            <div className="flex flex-col gap-4">
              {currencies.map((currency) => (
                <CurrencyForm key={currency.code} locale={locale} texts={texts} base={base} currency={currency} />
              ))}
            </div>
          </AdminCard>
          <AdminCard title={texts.addCurrencyTitle} id="currencies-add">
            <AddCurrencyForm locale={locale} texts={texts} base={base} />
          </AdminCard>
        </div>
        <div className="flex flex-col gap-4">
          <AdminCard title={texts.localeDefaultsTitle} id="currencies-locales">
            <LocaleDefaultsForm locale={locale} texts={texts} defaults={overview?.localeDefaults ?? []} currencies={currencies} languageNames={languageNames} />
          </AdminCard>
          <AdminCard title={texts.syncTitle} id="currencies-sync">
            <SyncRatesPanel locale={locale} texts={texts} source={overview?.rateSource ?? "None"} />
          </AdminCard>
        </div>
      </div>
    </AdminShell>
  );
}

export default async function CurrenciesPage({ params }: PageProps<"/[locale]/admin/currencies">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Currencies locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
