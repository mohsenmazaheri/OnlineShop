"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import type { ErrorTexts } from "@/lib/account/problems";
import { signedQuantity } from "@/lib/admin/inventory";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";

type LineView = components["schemas"]["StocktakeLineView"];
type Texts = Dictionary["admin"];

type Props = { locale: string; texts: Texts; number: number };

/**
 * Quick count entry for a scanner or a keyboard: SKU, quantity, Enter. In add mode each entry adds to the count so
 * far (scan every piece with quantity 1). After a save the SKU field is cleared and focused for the next item.
 */
export function StocktakeCountForm({ locale, texts, number }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.inventoryErrors } as ErrorTexts);
  const [saved, setSaved] = useState<LineView | null>(null);
  const [add, setAdd] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(null);
    const element = event.currentTarget;
    const form = new FormData(element);
    const quantity = Number.parseInt(String(form.get("quantity") ?? ""), 10);
    const result = await submit<LineView[]>(`/api/v1/admin/inventory/stocktakes/${number}/counts?locale=${locale}`, {
      counts: [{ sku: String(form.get("sku") ?? "").trim(), quantity: Number.isNaN(quantity) ? -1 : quantity }],
      add,
    });
    if (!result.ok) return;
    setSaved(result.data[0] ?? null);
    element.reset();
    document.getElementById("count-sku")?.focus();
    router.refresh();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      <p className="text-sm text-muted">{texts.stocktakeCountHint}</p>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && (
        <FormAlert kind="success">
          {texts.stocktakeCountSaved
            .replace("{sku}", saved.sku)
            .replace("{counted}", formatNumber(saved.counted ?? 0, locale))
            .replace("{difference}", signedQuantity(saved.difference ?? 0, locale))}
        </FormAlert>
      )}
      <div className="grid gap-3 sm:grid-cols-[2fr_1fr]">
        <FormField label={texts.movementSku} name="sku" id="count-sku" required dir="ltr" autoComplete="off" />
        <FormField
          label={texts.movementQuantity}
          name="quantity"
          id="count-quantity"
          type="number"
          inputMode="numeric"
          min={0}
          step={1}
          required
          dir="ltr"
          defaultValue={add ? "1" : undefined}
          key={add ? "add" : "set"}
        />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={add} onChange={(event) => setAdd(event.target.checked)} className="h-5 w-5" />
        {texts.stocktakeAddMode}
      </label>
      <div>
        <SubmitButton busy={busy} label={texts.stocktakeCountSave} busyLabel="…" />
      </div>
    </form>
  );
}
