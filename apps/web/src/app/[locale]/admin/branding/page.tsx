import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { BrandingForm } from "@/components/admin/BrandingForm";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getBrandingAdmin } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { toBranding } from "@/lib/store/settings";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/branding">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.brandingTitle);
}

/** The shop's look: main colour, corners, font and home page layout (permission settings.manage). */
async function Branding({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "settings.manage", `/${locale}/admin/branding`);
  const branding = toBranding(await getBrandingAdmin());

  return (
    <AdminShell locale={locale} texts={texts} current="branding" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.brandingIntro}</p>
      <AdminCard title={texts.brandingTitle} id="branding-form">
        <BrandingForm locale={locale} texts={texts} branding={branding} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function BrandingPage({ params }: PageProps<"/[locale]/admin/branding">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Branding locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
