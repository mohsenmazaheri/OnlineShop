"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import type { MyStaffInvitation } from "@/lib/account/session";
import { format } from "@/lib/account/digits";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatList } from "@/lib/i18n/format-list";
import { roleName } from "@/components/admin/RoleCheckboxes";
import { FormAlert } from "./FormParts";
import { useApiForm } from "./useApiForm";

type Props = {
  locale: string;
  texts: Dictionary["account"];
  adminTexts: Dictionary["admin"];
  invitations: readonly MyStaffInvitation[];
};

/** Invitations to work in the back office, shown to the invitee on their account page. */
export function StaffInvitations({ locale, texts, adminTexts, invitations }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm(adminTexts.errors);
  const [accepted, setAccepted] = useState(false);

  async function accept(id: string) {
    const result = await submit(`/api/v1/account/staff-invitations/${id}/accept`, undefined);
    if (result.ok) {
      setAccepted(true);
      router.refresh();
    }
  }

  if (accepted)
    return (
      <FormAlert kind="success">
        {texts.invitationAccepted}{" "}
        <Link href={`/${locale}/admin`} className="underline">
          {texts.adminPanel}
        </Link>
      </FormAlert>
    );
  if (invitations.length === 0) return null;

  return (
    <section aria-labelledby="staff-invitations" className="rounded-xl border border-primary/40 bg-primary/5 p-3">
      <h2 id="staff-invitations" className="mb-2 font-semibold">
        {texts.staffInvitationsTitle}
      </h2>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <ul className="flex flex-col gap-3">
        {invitations.map((invitation) => (
          <li key={invitation.id} className="flex flex-col gap-2 text-sm">
            <p>
              {format(texts.staffInvitationText, {
                roles: formatList(invitation.roles.map((r) => roleName(adminTexts, r)), locale),
                date: formatDate(invitation.expiresUtc, locale),
              })}
            </p>
            <button
              type="button"
              disabled={busy}
              onClick={() => accept(invitation.id)}
              className="h-11 self-start rounded-xl bg-primary px-4 font-semibold text-primary-foreground disabled:opacity-60"
            >
              {texts.acceptInvitation}
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}
