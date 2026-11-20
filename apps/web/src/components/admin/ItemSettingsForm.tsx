"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import type { ErrorTexts } from "@/lib/account/problems";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { parseNumber } from "./ProductEditor";

type Texts = Dictionary["admin"];

/**
 * An item's minimum stock (empty = no alert) and, for stock that came in without a purchase receipt, a starting
 * average cost (empty = unchanged).
 */
export function ItemSettingsForm({ texts, currency, sku }: { texts: Texts; currency: string; sku: string }) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm({ ...texts.errors, ...texts.inventoryErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const form = new FormData(event.currentTarget);
    const minimum = parseNumber(form.get("minimumStock"));
    const body = {
      sku: String(form.get("sku") ?? "").trim(),
      minimumStock: minimum === null ? null : Math.trunc(minimum),
      averageCost: parseNumber(form.get("averageCost")),
    };
    const result = await submit("/api/v1/admin/inventory/items/settings", body, "PUT");
    if (!result.ok) return;
    setSaved(true);
    router.refresh();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.itemSettingsSaved}</FormAlert>}
      <div className="grid gap-3 sm:grid-cols-3">
        <FormField label={texts.movementSku} name="sku" id="settings-sku" defaultValue={sku} required dir="ltr" autoComplete="off" error={fieldError("sku")} />
        <FormField label={texts.itemSettingsMinimum} name="minimumStock" id="settings-minimum" inputMode="numeric" dir="ltr" error={fieldError("minimumStock")} />
        <FormField
          label={texts.itemSettingsCost.replace("{currency}", currency)}
          name="averageCost"
          id="settings-cost"
          inputMode="decimal"
          dir="ltr"
          error={fieldError("averageCost")}
        />
      </div>
      <div>
        <SubmitButton busy={busy} label={texts.itemSettingsSave} busyLabel="…" />
      </div>
    </form>
  );
}
