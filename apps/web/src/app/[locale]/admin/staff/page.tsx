import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { InviteStaffForm } from "@/components/admin/InviteStaffForm";
import { StaffAuditLog } from "@/components/admin/StaffAuditLog";
import { StaffInvitationsList } from "@/components/admin/StaffInvitationsList";
import { StaffMembers } from "@/components/admin/StaffMembers";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getStaffAudit, getStaffOverview } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/staff">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.staffTitle);
}

/** Team, invitations and the activity log; administrators only (permission staff.manage). */
async function Staff({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "staff.manage", `/${locale}/admin/staff`);
  const [overview, audit] = await Promise.all([getStaffOverview(), getStaffAudit()]);

  return (
    <AdminShell locale={locale} texts={texts} current="staff" permissions={user.permissions}>
      <div className="grid gap-4 lg:grid-cols-3">
        <div className="flex flex-col gap-4 lg:col-span-2">
          <AdminCard title={texts.membersTitle} id="staff-members">
            <StaffMembers locale={locale} texts={texts} members={overview?.members ?? []} currentUserId={user.id} />
          </AdminCard>
          <AdminCard title={texts.invitationsTitle} id="staff-invitations">
            <StaffInvitationsList locale={locale} texts={texts} invitations={overview?.invitations ?? []} />
          </AdminCard>
          <AdminCard title={texts.auditTitle} id="staff-audit">
            <StaffAuditLog locale={locale} texts={texts} entries={audit ?? []} />
          </AdminCard>
        </div>
        <AdminCard title={texts.inviteTitle} id="staff-invite">
          <InviteStaffForm locale={locale} texts={texts} />
        </AdminCard>
      </div>
    </AdminShell>
  );
}

export default async function StaffPage({ params }: PageProps<"/[locale]/admin/staff">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Staff locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
