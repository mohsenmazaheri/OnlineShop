"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField, SelectField } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { parseNumber } from "./ProductEditor";

type ReceiptView = components["schemas"]["ReceiptView"];
type SupplierView = components["schemas"]["SupplierView"];
type WarehouseView = components["schemas"]["WarehouseView"];
type Texts = Dictionary["admin"];

export type LineDraft = { key: number; sku: string; quantity: string; unitCost: string };

/** The lines as the API takes them; rows without a SKU are left out (numbers the server checks). */
export function toLines(rows: LineDraft[]): { sku: string; quantity: number; unitCost: number }[] {
  return rows
    .filter((r) => r.sku.trim())
    .map((r) => ({ sku: r.sku.trim(), quantity: Math.trunc(parseNumber(r.quantity) ?? 0), unitCost: parseNumber(r.unitCost) ?? -1 }));
}

type Props = {
  locale: string;
  texts: Texts;
  currency: string;
  suppliers: SupplierView[];
  warehouses: WarehouseView[];
  receipt?: ReceiptView;
};

/**
 * A draft purchase receipt: supplier, warehouse, the supplier's invoice number and the lines (SKU, quantity, unit
 * cost). Saving sends the whole draft; totals come back from the server. Posting saves first, then brings the goods in.
 */
export function ReceiptEditor({ locale, texts, currency, suppliers, warehouses, receipt }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.inventoryErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);
  const [rows, setRows] = useState<LineDraft[]>(() =>
    receipt && receipt.lines.length > 0
      ? receipt.lines.map((l, i) => ({ key: i, sku: l.sku, quantity: String(l.quantity), unitCost: String(l.unitCost) }))
      : [{ key: 0, sku: "", quantity: "", unitCost: "" }],
  );
  const activeSuppliers = suppliers.filter((s) => s.isActive || s.id === receipt?.summary.supplierId);
  const activeWarehouses = warehouses.filter((w) => w.isActive);

  const change = (key: number, field: keyof Omit<LineDraft, "key">, value: string) =>
    setRows((current) => current.map((r) => (r.key === key ? { ...r, [field]: value } : r)));
  const addRow = () => setRows((current) => [...current, { key: Math.max(-1, ...current.map((r) => r.key)) + 1, sku: "", quantity: "", unitCost: "" }]);
  const removeRow = (key: number) => setRows((current) => current.filter((r) => r.key !== key));

  async function save(form: HTMLFormElement): Promise<ReceiptView | null> {
    const data = new FormData(form);
    const text = (name: string) => String(data.get(name) ?? "").trim() || null;
    const body = {
      supplierId: text("supplierId") ?? "",
      warehouseId: text("warehouseId") ?? "",
      supplierReference: text("supplierReference"),
      note: text("note"),
      lines: toLines(rows),
    };
    const result = receipt
      ? await submit<ReceiptView>(`/api/v1/admin/inventory/purchases/${receipt.summary.number}?locale=${locale}`, body, "PUT")
      : await submit<ReceiptView>(`/api/v1/admin/inventory/purchases?locale=${locale}`, body);
    return result.ok ? result.data : null;
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const view = await save(event.currentTarget);
    if (!view) return;
    if (!receipt) {
      router.push(`/${locale}/admin/purchases/${view.summary.number}`);
      return;
    }
    setSaved(true);
    router.refresh();
  }

  async function post(form: HTMLFormElement | null) {
    if (!form || !receipt || !window.confirm(texts.purchaseConfirmPost)) return;
    if (!(await save(form))) return;
    const result = await submit(`/api/v1/admin/inventory/purchases/${receipt.summary.number}/post?locale=${locale}`, undefined);
    if (result.ok) router.refresh();
  }

  async function cancel() {
    if (!receipt || !window.confirm(texts.purchaseConfirmCancel)) return;
    const result = await submit(`/api/v1/admin/inventory/purchases/${receipt.summary.number}/cancel?locale=${locale}`, undefined);
    if (result.ok) router.refresh();
  }

  if (activeSuppliers.length === 0) return <p className="text-sm text-muted">{texts.purchaseNoSuppliers}</p>;

  const cell = "h-11 w-full rounded-xl border border-border bg-surface px-3";
  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.saved}</FormAlert>}
      <div className="grid gap-3 sm:grid-cols-2">
        <SelectField
          label={texts.purchaseSupplier}
          name="supplierId"
          id="receipt-supplier"
          options={activeSuppliers.map((s) => ({ value: s.id, label: s.name }))}
          defaultValue={receipt?.summary.supplierId}
        />
        <SelectField
          label={texts.movementWarehouse}
          name="warehouseId"
          id="receipt-warehouse"
          options={activeWarehouses.map((w) => ({ value: w.id, label: w.name }))}
          defaultValue={receipt?.summary.warehouseId ?? activeWarehouses.find((w) => w.isDefault)?.id}
        />
        <FormField
          label={texts.purchaseSupplierRef}
          name="supplierReference"
          id="receipt-reference"
          defaultValue={receipt?.summary.supplierReference ?? ""}
          maxLength={100}
        />
        <FormField label={texts.movementNote} name="note" id="receipt-note" defaultValue={receipt?.note ?? ""} maxLength={500} />
      </div>

      <fieldset className="flex flex-col gap-2">
        <legend className="mb-2 text-sm font-medium">{texts.purchaseLines}</legend>
        {rows.map((row, i) => (
          <div key={row.key} className="grid grid-cols-[1fr_5rem_7rem_auto] items-center gap-2">
            <input
              aria-label={`${texts.movementSku} ${i + 1}`}
              value={row.sku}
              onChange={(event) => change(row.key, "sku", event.target.value)}
              dir="ltr"
              autoComplete="off"
              placeholder="SKU"
              className={cell}
            />
            <input
              aria-label={`${texts.movementQuantity} ${i + 1}`}
              value={row.quantity}
              onChange={(event) => change(row.key, "quantity", event.target.value)}
              inputMode="numeric"
              dir="ltr"
              placeholder={texts.movementQuantity}
              className={cell}
            />
            <input
              aria-label={`${texts.purchaseUnitCost.replace("{currency}", currency)} ${i + 1}`}
              value={row.unitCost}
              onChange={(event) => change(row.key, "unitCost", event.target.value)}
              inputMode="decimal"
              dir="ltr"
              placeholder={currency}
              className={cell}
            />
            <Button variant="ghost" size="sm" onClick={() => removeRow(row.key)} disabled={rows.length === 1} aria-label={`${texts.purchaseRemoveLine} ${i + 1}`}>
              ×
            </Button>
          </div>
        ))}
        <div>
          <Button variant="secondary" size="sm" onClick={addRow}>
            {texts.purchaseAddLine}
          </Button>
        </div>
        <p className="text-xs text-muted">{texts.purchaseTotalsNote}</p>
      </fieldset>

      <div className="flex flex-wrap gap-2 border-t border-border pt-4">
        <Button type="submit" variant={receipt ? "secondary" : "primary"} disabled={busy}>
          {texts.purchaseSave}
        </Button>
        {receipt && (
          <>
            <Button onClick={(event) => post(event.currentTarget.form)} disabled={busy}>
              {texts.purchasePost}
            </Button>
            <Button variant="danger" onClick={cancel} disabled={busy}>
              {texts.purchaseCancel}
            </Button>
          </>
        )}
      </div>
    </form>
  );
}
