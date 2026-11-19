import Link from "next/link";
import { Suspense } from "react";
import { AdminShell } from "@/components/admin/AdminShell";
import { Icon } from "@/components/ui/Icon";
import { requireStaff } from "@/lib/admin/access";
import { adminHref, sectionsByGroup } from "@/lib/admin/sections";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.title);
}

/** Start page of the admin panel: a tile for every section this staff member may open, grouped. */
async function AdminHome({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requireStaff(locale, `/${locale}/admin`);
  const groups = sectionsByGroup(user.permissions);

  return (
    <AdminShell locale={locale} texts={texts} current="home" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.homeIntro}</p>
      {groups.map(({ group, sections }) => (
        <section key={group} aria-labelledby={`group-${group}`} className="flex flex-col gap-3">
          <h2 id={`group-${group}`} className="text-base font-bold">
            {texts.groups[group]}
          </h2>
          <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {sections.map((s) => (
              <li key={s.section}>
                <Link
                  href={adminHref(locale, s.path)}
                  className="flex h-full items-start gap-3 rounded-2xl border border-border bg-surface p-4 hover:border-primary hover:shadow-sm"
                >
                  <span aria-hidden className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary">
                    <Icon name={s.icon} size={22} />
                  </span>
                  <span className="flex flex-col gap-1">
                    <span className="font-bold">{texts[s.label as keyof typeof texts] as string}</span>
                    <span className="text-sm text-muted">{texts.sectionHints[s.section]}</span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </AdminShell>
  );
}

export default async function AdminPage({ params }: PageProps<"/[locale]/admin">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <AdminHome locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
