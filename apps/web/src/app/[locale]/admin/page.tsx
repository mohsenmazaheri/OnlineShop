import Link from "next/link";
import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { requireStaff } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.title);
}

/** Start page of the admin panel: the sections this staff member may open. */
async function AdminHome({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requireStaff(locale, `/${locale}/admin`);
  return (
    <AdminShell locale={locale} texts={texts} current="home" permissions={user.permissions}>
      <AdminCard title={texts.navHome} id="admin-home">
        <p className="mb-3 text-sm text-muted">{texts.homeIntro}</p>
        {user.permissions.includes("staff.manage") && (
          <Link href={`/${locale}/admin/staff`} className="inline-flex h-11 items-center rounded-xl border border-border px-4 font-medium hover:bg-background">
            {texts.navStaff}
          </Link>
        )}
      </AdminCard>
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
