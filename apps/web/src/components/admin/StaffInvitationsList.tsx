"use client";

import { useRouter } from "next/navigation";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { format } from "@/lib/account/digits";
import { formatDate } from "@/lib/i18n/format-date";
import { formatList } from "@/lib/i18n/format-list";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { roleName } from "./RoleCheckboxes";

type Invitation = components["schemas"]["StaffInvitationView"];

/** Recent invitations with their status; open ones can be withdrawn. */
export function StaffInvitationsList({ locale, texts, invitations }: { locale: string; texts: Dictionary["admin"]; invitations: readonly Invitation[] }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm(texts.errors);

  async function revoke(invitation: Invitation) {
    if (!window.confirm(texts.confirmRevoke)) return;
    const result = await submit(`/api/v1/admin/staff/invitations/${invitation.id}`, undefined, "DELETE");
    if (result.ok) router.refresh();
  }

  if (invitations.length === 0) return <p className="text-sm text-muted">{texts.noInvitations}</p>;

  return (
    <div className="flex flex-col gap-2">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <ul className="flex flex-col gap-2">
        {invitations.map((invitation) => (
          <li key={invitation.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border border-border p-2 text-sm">
            <span dir="ltr" className="font-medium">{invitation.email ?? invitation.phoneNumber}</span>
            <span>{formatList(invitation.roles.map((r) => roleName(texts, r)), locale)}</span>
            <span className="rounded-full bg-background px-2 py-0.5 text-xs">{(texts.invitationStatus as Record<string, string>)[invitation.status]}</span>
            {invitation.status === "Pending" && (
              <>
                <span className="text-xs text-muted">{format(texts.validUntil, { date: formatDate(invitation.expiresUtc, locale) })}</span>
                <button type="button" className="text-red-600 underline dark:text-red-400" disabled={busy} onClick={() => revoke(invitation)}>
                  {texts.revoke}
                </button>
              </>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
