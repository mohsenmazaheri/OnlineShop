import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ContentEditor } from "@/components/admin/ContentEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getContentList } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/content">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.contentTitle);
}

/** The blog and content pages (content.manage). */
async function Content({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "content.manage", `/${locale}/admin/content`);
  const list = (await getContentList()) ?? { defaultLocale: locale, locales: [locale], items: [] };

  return (
    <AdminShell locale={locale} texts={texts} current="content" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.contentIntro}</p>
      <AdminCard title={texts.contentTitle} id="content">
        <ContentEditor locale={locale} texts={texts} list={list} languageNames={languageNames(list.locales)} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function ContentAdminPage({ params }: PageProps<"/[locale]/admin/content">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Content locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
