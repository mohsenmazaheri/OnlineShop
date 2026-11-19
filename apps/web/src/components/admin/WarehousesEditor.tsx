"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import { Icon } from "@/components/ui/Icon";
import { Badge } from "@/components/ui/Surface";
import type { ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";

type WarehouseView = components["schemas"]["WarehouseView"];
type Texts = Dictionary["admin"];

type FormProps = { texts: Texts; warehouse?: WarehouseView };

/** One warehouse's form (or "add"). The server keeps the rules: one active default, no deactivating with stock. */
function WarehouseForm({ texts, warehouse }: FormProps) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm({ ...texts.errors, ...texts.inventoryErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);
  const prefix = `warehouse-${warehouse?.id ?? "new"}-`;

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const element = event.currentTarget;
    const form = new FormData(element);
    const body = {
      code: String(form.get("code") ?? "").trim() || null,
      name: String(form.get("name") ?? "").trim(),
      address: String(form.get("address") ?? "").trim() || null,
      isActive: form.get("isActive") === "on",
      isDefault: form.get("isDefault") === "on",
    };
    const result = await submit(warehouse ? `/api/v1/admin/inventory/warehouses/${warehouse.id}` : "/api/v1/admin/inventory/warehouses", body, warehouse ? "PUT" : "POST");
    if (!result.ok) return;
    setSaved(true);
    if (!warehouse) element.reset();
    router.refresh();
  }

  async function remove() {
    if (!warehouse || !window.confirm(texts.confirmDeleteWarehouse)) return;
    const result = await submit(`/api/v1/admin/inventory/warehouses/${warehouse.id}`, undefined, "DELETE");
    if (result.ok) router.refresh();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && warehouse && <FormAlert kind="success">{texts.saved}</FormAlert>}
      <div className="grid gap-3 sm:grid-cols-2">
        <FormField label={texts.warehouseName} name="name" id={`${prefix}name`} defaultValue={warehouse?.name} required maxLength={100} error={fieldError("name")} />
        <FormField
          label={texts.warehouseCode}
          name="code"
          id={`${prefix}code`}
          defaultValue={warehouse?.code}
          dir="ltr"
          maxLength={64}
          hint={texts.warehouseCodeHint}
          error={fieldError("code")}
        />
        <FormField label={texts.warehouseAddress} name="address" id={`${prefix}address`} defaultValue={warehouse?.address ?? ""} maxLength={300} className="sm:col-span-2" />
      </div>
      <div className="flex flex-wrap gap-4 text-sm">
        <label className="flex items-center gap-2">
          <input type="checkbox" name="isActive" defaultChecked={warehouse?.isActive ?? true} disabled={warehouse?.isDefault} className="h-5 w-5" />
          {texts.warehouseActive}
        </label>
        {/* A disabled box is not submitted: the default stays active and default through hidden fields. */}
        {warehouse?.isDefault && (
          <>
            <input type="hidden" name="isActive" value="on" />
            <input type="hidden" name="isDefault" value="on" />
          </>
        )}
        <label className="flex items-center gap-2">
          <input type="checkbox" name="isDefault" defaultChecked={warehouse?.isDefault ?? false} disabled={warehouse?.isDefault} className="h-5 w-5" />
          {texts.warehouseDefault}
        </label>
      </div>
      <div className="flex flex-wrap gap-2">
        <SubmitButton busy={busy} label={warehouse ? texts.save : texts.add} busyLabel="…" />
        {warehouse && !warehouse.isDefault && !warehouse.used && (
          <Button variant="danger" onClick={remove} disabled={busy}>
            {texts.remove}
          </Button>
        )}
      </div>
    </form>
  );
}

type Props = { locale: string; texts: Texts; warehouses: WarehouseView[]; canManage: boolean };

/** The shop's warehouses with their stock; each opens to its form, plus "add" (with inventory.manage). */
export function WarehousesEditor({ locale, texts, warehouses, canManage }: Props) {
  return (
    <div className="flex flex-col gap-3">
      {!canManage && <p className="text-sm text-muted">{texts.warehouseReadOnly}</p>}
      <ul className="flex flex-col gap-2">
        {warehouses.map((w) => {
          const summary = (
            <span className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
              <span className="font-medium">{w.name}</span>
              <span dir="ltr" className="text-xs text-muted">
                {w.code}
              </span>
              {w.isDefault && <Badge tone="primary">{texts.warehouseDefault}</Badge>}
              {!w.isActive && <Badge>{texts.warehouseInactive}</Badge>}
              <span className="text-xs text-muted">
                {texts.warehouseStock.replace("{units}", formatNumber(w.units, locale)).replace("{items}", formatNumber(w.items, locale))}
              </span>
            </span>
          );
          return (
            <li key={w.id} className="rounded-2xl border border-border bg-surface">
              {canManage ? (
                <details className="group">
                  <summary className="flex cursor-pointer list-none items-center gap-2 p-3 [&::-webkit-details-marker]:hidden">
                    {summary}
                    <Icon name="chevron" size={18} className="shrink-0 text-muted transition-transform group-open:rotate-90 rtl:-scale-x-100 rtl:group-open:rotate-90" />
                  </summary>
                  <div className="border-t border-border p-3">
                    <WarehouseForm texts={texts} warehouse={w} />
                  </div>
                </details>
              ) : (
                <div className="p-3">{summary}</div>
              )}
            </li>
          );
        })}
      </ul>
      {canManage && (
        <details className="rounded-2xl border border-dashed border-border">
          <summary className="cursor-pointer p-3 font-medium">+ {texts.warehouseNew}</summary>
          <div className="border-t border-border p-3">
            <WarehouseForm texts={texts} />
          </div>
        </details>
      )}
    </div>
  );
}
