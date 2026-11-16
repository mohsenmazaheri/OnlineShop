"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { format } from "@/lib/account/digits";
import { formatList } from "@/lib/i18n/format-list";
import { FormAlert, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { RoleCheckboxes, roleName } from "./RoleCheckboxes";

type Member = components["schemas"]["StaffMember"];
type Texts = Dictionary["admin"];

/** Team members with their roles and status; actions for everyone except the signed-in administrator. */
type Props = { locale: string; texts: Texts; members: readonly Member[]; currentUserId: string };

export function StaffMembers({ locale, texts, members, currentUserId }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm(texts.errors);
  const [editing, setEditing] = useState<string | null>(null);

  async function act(member: Member, action: "suspend" | "reactivate" | "remove", confirmText?: string) {
    if (confirmText && !window.confirm(format(confirmText, { name: label(member) }))) return;
    const result = await submit(`/api/v1/admin/staff/${encodeURIComponent(member.userId)}/${action}`, undefined);
    if (result.ok) router.refresh();
  }

  if (members.length === 0) return <p className="text-sm text-muted">{texts.noMembers}</p>;

  return (
    <div className="flex flex-col gap-3">
      {formError && editing === null && <FormAlert kind="error">{formError}</FormAlert>}
      <ul className="flex flex-col gap-3">
        {members.map((member) => {
          const self = member.userId === currentUserId;
          return (
            <li key={member.userId} className="rounded-xl border border-border p-3 text-sm">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-semibold">{label(member)}</span>
                {self && <span className="text-muted">{texts.you}</span>}
                <span className={`rounded-full px-2 py-0.5 text-xs ${member.suspended ? "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200" : "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200"}`}>
                  {member.suspended ? texts.suspended : texts.active}
                </span>
              </div>
              <p dir="ltr" className="text-start text-muted">
                {[member.email, member.phoneNumber].filter(Boolean).join(" · ")}
              </p>
              <p className="mt-1">{formatList(member.roles.map((r) => roleName(texts, r)), locale)}</p>
              <p className="text-xs text-muted">{member.twoFactorEnabled ? texts.twoFactorOn : texts.twoFactorOff}</p>

              {editing === member.userId ? (
                <RolesEditor texts={texts} member={member} onDone={() => { setEditing(null); router.refresh(); }} onCancel={() => setEditing(null)} />
              ) : (
                !self && (
                  <div className="mt-2 flex flex-wrap gap-3">
                    <button type="button" className="text-primary underline" onClick={() => setEditing(member.userId)}>
                      {texts.editRoles}
                    </button>
                    {member.suspended ? (
                      <button type="button" className="text-primary underline" disabled={busy} onClick={() => act(member, "reactivate")}>
                        {texts.reactivate}
                      </button>
                    ) : (
                      <button type="button" className="text-amber-700 underline dark:text-amber-300" disabled={busy} onClick={() => act(member, "suspend", texts.confirmSuspend)}>
                        {texts.suspend}
                      </button>
                    )}
                    <button type="button" className="text-red-600 underline dark:text-red-400" disabled={busy} onClick={() => act(member, "remove", texts.confirmRemove)}>
                      {texts.remove}
                    </button>
                  </div>
                )
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

function label(member: Member): string {
  return member.displayName ?? member.email ?? member.phoneNumber ?? member.userId;
}

function RolesEditor({ texts, member, onDone, onCancel }: { texts: Texts; member: Member; onDone: () => void; onCancel: () => void }) {
  const { busy, submit, formError, fieldError } = useApiForm(texts.errors);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const roles = new FormData(event.currentTarget).getAll("roles").map(String);
    const result = await submit(`/api/v1/admin/staff/${encodeURIComponent(member.userId)}/roles`, { roles }, "PUT");
    if (result.ok) onDone();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="mt-3 flex flex-col gap-3 rounded-lg bg-background p-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <RoleCheckboxes texts={texts} name="roles" defaultChecked={member.roles} error={fieldError("roles")} />
      <div className="flex flex-wrap items-center gap-3">
        <SubmitButton busy={busy} label={texts.saveRoles} busyLabel="…" />
        <button type="button" className="text-sm text-primary underline" onClick={onCancel}>
          {texts.cancel}
        </button>
      </div>
    </form>
  );
}
