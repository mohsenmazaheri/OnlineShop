"use client";

import { useRouter } from "next/navigation";
import type { FormEvent } from "react";
import { FormAlert, FormField, SelectField, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import type { ErrorTexts } from "@/lib/account/problems";
import { stocktakeHref } from "@/lib/admin/inventory";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";

type WarehouseView = components["schemas"]["WarehouseView"];
type StocktakeSummary = components["schemas"]["StocktakeSummary"];
type Texts = Dictionary["admin"];

/** Start counting an active warehouse; the new stocktake's page opens. */
export function OpenStocktakeForm({ locale, texts, warehouses }: { locale: string; texts: Texts; warehouses: WarehouseView[] }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.inventoryErrors } as ErrorTexts);
  const active = warehouses.filter((w) => w.isActive);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const note = String(form.get("note") ?? "").trim();
    const result = await submit<StocktakeSummary>("/api/v1/admin/inventory/stocktakes", { warehouseId: String(form.get("warehouseId") ?? ""), note: note || null });
    if (result.ok) router.push(stocktakeHref(locale, result.data.number));
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <div className="grid gap-3 sm:grid-cols-2">
        <SelectField
          label={texts.movementWarehouse}
          name="warehouseId"
          id="stocktake-warehouse"
          options={active.map((w) => ({ value: w.id, label: w.name }))}
          defaultValue={active.find((w) => w.isDefault)?.id}
        />
        <FormField label={texts.stocktakeNote} name="note" id="stocktake-note" maxLength={500} />
      </div>
      <div>
        <SubmitButton busy={busy} label={texts.stocktakeStart} busyLabel="…" />
      </div>
    </form>
  );
}
