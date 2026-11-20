import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ReservationsList } from "@/components/admin/ReservationsList";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getReservations } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/inventory/reservations">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.reservationsTitle);
}

/** Units held for unpaid checkouts (inventory.view; releasing needs inventory.manage). */
async function Reservations({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/inventory/reservations`);
  const reservations = (await getReservations(locale)) ?? [];

  return (
    <AdminShell locale={locale} texts={texts} current="reservations" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.reservationsIntro}</p>
      <AdminCard title={texts.reservationsTitle} id="reservations">
        <ReservationsList locale={locale} texts={texts} reservations={reservations} canManage={user.permissions.includes("inventory.manage")} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function ReservationsPage({ params }: PageProps<"/[locale]/admin/inventory/reservations">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Reservations locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
