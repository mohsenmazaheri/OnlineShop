"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { FormAlert, FormField, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { RoleCheckboxes } from "./RoleCheckboxes";

/** Invite a colleague by email or mobile number with one or more roles. */
export function InviteStaffForm({ locale, texts }: { locale: string; texts: Dictionary["admin"] }) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm(texts.errors);
  const [done, setDone] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setDone(false);
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const result = await submit("/api/v1/admin/staff/invitations", {
      identifier: String(form.get("identifier") ?? "").trim(),
      roles: form.getAll("roles").map(String),
      locale,
    });
    if (result.ok) {
      setDone(true);
      formElement.reset();
      router.refresh();
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {done && <FormAlert kind="success">{texts.invited}</FormAlert>}
      <FormField label={texts.inviteIdentifier} name="identifier" dir="ltr" autoComplete="off" required hint={texts.inviteHint} error={fieldError("identifier")} />
      <RoleCheckboxes texts={texts} name="roles" error={fieldError("roles")} />
      <SubmitButton busy={busy} label={texts.invite} busyLabel="…" />
    </form>
  );
}
