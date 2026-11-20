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

type SupplierView = components["schemas"]["SupplierView"];
type Texts = Dictionary["admin"];

/** One supplier's form (or "add"). A supplier with receipts can only be deactivated, not deleted. */
function SupplierForm({ texts, supplier }: { texts: Texts; supplier?: SupplierView }) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm({ ...texts.errors, ...texts.inventoryErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);
  const prefix = `supplier-${supplier?.id ?? "new"}-`;

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const element = event.currentTarget;
    const form = new FormData(element);
    const text = (name: string) => String(form.get(name) ?? "").trim() || null;
    const body = {
      name: text("name") ?? "",
      contactName: text("contactName"),
      phone: text("phone"),
      email: text("email"),
      address: text("address"),
      note: text("note"),
      isActive: form.get("isActive") === "on",
    };
    const result = await submit(supplier ? `/api/v1/admin/inventory/suppliers/${supplier.id}` : "/api/v1/admin/inventory/suppliers", body, supplier ? "PUT" : "POST");
    if (!result.ok) return;
    setSaved(true);
    if (!supplier) element.reset();
    router.refresh();
  }

  async function remove() {
    if (!supplier || !window.confirm(texts.confirmDeleteSupplier)) return;
    const result = await submit(`/api/v1/admin/inventory/suppliers/${supplier.id}`, undefined, "DELETE");
    if (result.ok) router.refresh();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && supplier && <FormAlert kind="success">{texts.saved}</FormAlert>}
      <div className="grid gap-3 sm:grid-cols-2">
        <FormField label={texts.supplierName} name="name" id={`${prefix}name`} defaultValue={supplier?.name} required maxLength={150} error={fieldError("name")} />
        <FormField label={texts.supplierContact} name="contactName" id={`${prefix}contact`} defaultValue={supplier?.contactName ?? ""} maxLength={150} />
        <FormField label={texts.supplierPhone} name="phone" id={`${prefix}phone`} defaultValue={supplier?.phone ?? ""} type="tel" dir="ltr" maxLength={30} />
        <FormField
          label={texts.supplierEmail}
          name="email"
          id={`${prefix}email`}
          defaultValue={supplier?.email ?? ""}
          type="email"
          dir="ltr"
          maxLength={254}
          error={fieldError("email")}
        />
        <FormField label={texts.warehouseAddress} name="address" id={`${prefix}address`} defaultValue={supplier?.address ?? ""} maxLength={300} />
        <FormField label={texts.movementNote} name="note" id={`${prefix}note`} defaultValue={supplier?.note ?? ""} maxLength={500} />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="isActive" defaultChecked={supplier?.isActive ?? true} className="h-5 w-5" />
        {texts.warehouseActive}
      </label>
      <div className="flex flex-wrap gap-2">
        <SubmitButton busy={busy} label={supplier ? texts.save : texts.add} busyLabel="…" />
        {supplier && supplier.receipts === 0 && (
          <Button variant="danger" onClick={remove} disabled={busy}>
            {texts.remove}
          </Button>
        )}
      </div>
    </form>
  );
}

type Props = { locale: string; texts: Texts; suppliers: SupplierView[]; canManage: boolean };

/** The shop's suppliers; each opens to its form, plus "add" (with inventory.manage). */
export function SuppliersEditor({ locale, texts, suppliers, canManage }: Props) {
  return (
    <div className="flex flex-col gap-3">
      {suppliers.length === 0 && <p className="text-sm text-muted">{texts.suppliersNone}</p>}
      <ul className="flex flex-col gap-2">
        {suppliers.map((s) => {
          const summary = (
            <span className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
              <span className="font-medium">{s.name}</span>
              {!s.isActive && <Badge>{texts.warehouseInactive}</Badge>}
              {s.phone && (
                <span dir="ltr" className="text-xs text-muted">
                  {s.phone}
                </span>
              )}
              <span className="text-xs text-muted">{texts.supplierReceipts.replace("{count}", formatNumber(s.receipts, locale))}</span>
            </span>
          );
          return (
            <li key={s.id} className="rounded-2xl border border-border bg-surface">
              {canManage ? (
                <details className="group">
                  <summary className="flex cursor-pointer list-none items-center gap-2 p-3 [&::-webkit-details-marker]:hidden">
                    {summary}
                    <Icon name="chevron" size={18} className="shrink-0 text-muted transition-transform group-open:rotate-90 rtl:-scale-x-100 rtl:group-open:rotate-90" />
                  </summary>
                  <div className="border-t border-border p-3">
                    <SupplierForm texts={texts} supplier={s} />
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
          <summary className="cursor-pointer p-3 font-medium">+ {texts.supplierNew}</summary>
          <div className="border-t border-border p-3">
            <SupplierForm texts={texts} />
          </div>
        </details>
      )}
    </div>
  );
}
