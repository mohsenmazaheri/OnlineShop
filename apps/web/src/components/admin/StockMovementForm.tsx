"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField, SelectField, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import type { ErrorTexts } from "@/lib/account/problems";
import { manualKinds } from "@/lib/admin/inventory";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";

type WarehouseView = components["schemas"]["WarehouseView"];
type StockRow = components["schemas"]["StockRow"];
type Texts = Dictionary["admin"];
type Mode = (typeof manualKinds)[number] | "Transfer";

type Props = { locale: string; texts: Texts; warehouses: WarehouseView[]; sku: string };

/**
 * Enter a receipt, an issue, an adjustment (signed) or a transfer for one item by its SKU. The server checks the
 * stock (never below zero) and answers with the item's stock afterwards; the page then reloads its table.
 */
export function StockMovementForm({ locale, texts, warehouses, sku }: Props) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm({ ...texts.errors, ...texts.inventoryErrors } as ErrorTexts);
  const [mode, setMode] = useState<Mode>("Receipt");
  const [saved, setSaved] = useState<StockRow | null>(null);
  const active = warehouses.filter((w) => w.isActive);
  const options = active.map((w) => ({ value: w.id, label: w.name }));
  const defaultId = active.find((w) => w.isDefault)?.id ?? active[0]?.id ?? "";
  const otherId = active.find((w) => w.id !== defaultId)?.id ?? defaultId;
  const modes: { value: Mode; label: string }[] = [
    ...manualKinds.map((k) => ({ value: k as Mode, label: texts.movementKinds[k] })),
    ...(active.length > 1 ? [{ value: "Transfer" as Mode, label: texts.movementTransfer }] : []),
  ];

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(null);
    const form = new FormData(event.currentTarget);
    const text = (name: string) => String(form.get(name) ?? "").trim();
    const quantity = Number.parseInt(text("quantity"), 10);
    const common = { sku: text("sku"), quantity: Number.isNaN(quantity) ? 0 : quantity, reference: text("reference") || null, note: text("note") || null };
    const result =
      mode === "Transfer"
        ? await submit<StockRow>(`/api/v1/admin/inventory/transfers?locale=${locale}`, {
            ...common,
            fromWarehouseId: text("fromWarehouseId"),
            toWarehouseId: text("toWarehouseId"),
          })
        : await submit<StockRow>(`/api/v1/admin/inventory/movements?locale=${locale}`, { ...common, kind: mode, warehouseId: text("warehouseId") });
    if (!result.ok) return;
    setSaved(result.data);
    router.refresh();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && (
        <FormAlert kind="success">{texts.movementSaved.replace("{sku}", saved.sku).replace("{total}", formatNumber(saved.total, locale))}</FormAlert>
      )}
      <div className="grid gap-3 sm:grid-cols-2">
        <FormField
          label={texts.movementSku}
          name="sku"
          id="movement-sku"
          defaultValue={sku}
          required
          dir="ltr"
          autoComplete="off"
          hint={texts.movementSkuHint}
          error={fieldError("sku")}
        />
        <SelectField
          label={texts.movementType}
          name="mode"
          id="movement-mode"
          options={modes}
          value={mode}
          onChange={(event) => setMode(event.target.value as Mode)}
        />
        {mode === "Transfer" ? (
          <>
            <SelectField label={texts.movementFrom} name="fromWarehouseId" id="movement-from" options={options} defaultValue={defaultId} />
            <SelectField label={texts.movementTo} name="toWarehouseId" id="movement-to" options={options} defaultValue={otherId} />
          </>
        ) : (
          <SelectField label={texts.movementWarehouse} name="warehouseId" id="movement-warehouse" options={options} defaultValue={defaultId} />
        )}
        <FormField
          label={texts.movementQuantity}
          name="quantity"
          id="movement-quantity"
          type="number"
          inputMode="numeric"
          step={1}
          min={mode === "Adjustment" ? undefined : 1}
          required
          dir="ltr"
          hint={mode === "Adjustment" ? texts.movementAdjustHint : undefined}
          error={fieldError("quantity")}
        />
        <FormField label={texts.movementReference} name="reference" id="movement-reference" maxLength={100} error={fieldError("reference")} />
        <FormField label={texts.movementNote} name="note" id="movement-note" maxLength={500} error={fieldError("note")} />
      </div>
      <div>
        <SubmitButton busy={busy} label={texts.movementSave} busyLabel="…" />
      </div>
    </form>
  );
}
