"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { OrderView } from "@/lib/account/session";
import type { Dictionary } from "@/lib/i18n/dictionaries";

type Texts = Dictionary["admin"];

type Props = { order: OrderView; texts: Texts; errors: ErrorTexts; canManage: boolean };

type Action = "paid" | "ship" | "complete" | "cancel" | "notes";

/**
 * The staff's next steps for one order: only the changes its status allows (the API checks them again), and an
 * internal note. Every change reloads the page, so the history and the buttons follow.
 */
export function OrderActionsPanel({ order, texts, errors, canManage }: Props) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm(errors);
  const [open, setOpen] = useState<Action | null>(null);
  const actions = order.actions;

  async function send(action: Action, body: unknown) {
    const result = await submit(`/api/v1/admin/orders/${order.number}/${action}`, body);
    if (!result.ok) return;
    setOpen(null);
    router.refresh();
  }

  function onSubmit(action: Action) {
    return async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      const form = new FormData(event.currentTarget);
      const text = (name: string) => String(form.get(name) ?? "").trim();
      if (action === "ship") await send("ship", { carrier: text("carrier"), trackingCode: text("trackingCode") });
      else if (action === "notes") await send("notes", { text: text("text") });
      else {
        if (action === "cancel" && !window.confirm(texts.orderConfirmCancel)) return;
        await send(action, { text: text("text") || null });
      }
    };
  }

  if (!canManage) return null;
  const buttons: { action: Action; label: string; show: boolean; variant?: "primary" | "secondary" | "danger" }[] = [
    { action: "paid", label: texts.orderMarkPaid, show: actions.canMarkPaid },
    { action: "ship", label: texts.orderShip, show: actions.canShip },
    { action: "complete", label: texts.orderComplete, show: actions.canComplete },
    { action: "cancel", label: texts.orderCancel, show: actions.canCancel, variant: "danger" },
    { action: "notes", label: texts.orderAddNote, show: true, variant: "secondary" },
  ];

  return (
    <section aria-labelledby="order-actions" className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
      <h2 id="order-actions" className="font-bold">
        {texts.orderActions}
      </h2>
      <div className="flex flex-wrap gap-2">
        {buttons
          .filter((b) => b.show)
          .map((b) => (
            <Button key={b.action} variant={b.variant ?? "primary"} aria-expanded={open === b.action} onClick={() => setOpen(open === b.action ? null : b.action)}>
              {b.label}
            </Button>
          ))}
      </div>
      {open && (
        <form onSubmit={onSubmit(open)} noValidate className="flex flex-col gap-3 rounded-xl border border-border p-3">
          {open === "ship" ? (
            <div className="grid gap-3 sm:grid-cols-2">
              <FormField label={texts.orderCarrier} name="carrier" required maxLength={100} error={fieldError("carrier")} />
              <FormField label={texts.orderTrackingCode} name="trackingCode" dir="ltr" required maxLength={100} error={fieldError("trackingCode")} />
            </div>
          ) : (
            <label className="flex flex-col gap-1 text-sm">
              <span className="font-medium">
                {open === "notes" ? texts.orderNote : open === "cancel" ? texts.orderCancelReason : open === "paid" ? texts.orderPaidNote : texts.orderOptionalNote}
              </span>
              <textarea name="text" rows={2} maxLength={1000} required={open === "notes"} className="rounded-xl border border-border bg-surface p-3" />
            </label>
          )}
          {open === "paid" && <p className="text-xs text-muted">{texts.orderMarkPaidHint}</p>}
          {formError && <FormAlert kind="error">{formError}</FormAlert>}
          <div>
            <Button type="submit" disabled={busy}>
              {texts.orderConfirm}
            </Button>
          </div>
        </form>
      )}
    </section>
  );
}
