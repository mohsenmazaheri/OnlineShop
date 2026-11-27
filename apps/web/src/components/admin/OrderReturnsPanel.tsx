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
type Step = "approve" | "reject" | "receive";

type Props = { order: OrderView; texts: Texts; errors: ErrorTexts; canManage: boolean };

/**
 * Staff's side of returns and refunds: answer each return (approve, reject, received) and record money paid back, at
 * most what is still refundable. The API checks every step again.
 */
export function OrderReturnsPanel({ order, texts, errors, canManage }: Props) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm(errors);
  const [open, setOpen] = useState<{ returnId: string; step: Step } | "refund" | null>(null);
  if (!canManage || (order.returns.length === 0 && !order.actions.canRefund)) return null;
  const received = order.returns.filter((r) => r.status === "Received");

  async function answer(event: FormEvent<HTMLFormElement>, returnId: string, step: Step) {
    event.preventDefault();
    const text = String(new FormData(event.currentTarget).get("text") ?? "").trim();
    const result = await submit(`/api/v1/admin/orders/${order.number}/returns/${returnId}/${step}`, { text: text || null });
    if (!result.ok) return;
    setOpen(null);
    router.refresh();
  }

  async function refund(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const returnId = String(form.get("returnId") ?? "");
    const result = await submit(`/api/v1/admin/orders/${order.number}/refunds`, {
      amount: Number(form.get("amount")),
      method: String(form.get("method") ?? "").trim(),
      note: String(form.get("note") ?? "").trim() || null,
      returnId: returnId || null,
    });
    if (!result.ok) return;
    setOpen(null);
    router.refresh();
  }

  const stepLabel: Record<Step, string> = { approve: texts.returnApprove, reject: texts.returnReject, receive: texts.returnReceive };
  return (
    <section aria-labelledby="order-returns-staff" className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
      <h2 id="order-returns-staff" className="font-bold">
        {texts.returnsTitle}
      </h2>
      {order.returns.map((r) => {
        const steps = ([r.canApprove && "approve", r.canReceive && "receive", r.canReject && "reject"] as const).filter((s): s is Step => Boolean(s));
        return (
          <div key={r.id} className="flex flex-col gap-2 rounded-xl border border-border p-3 text-sm">
            <p className="font-bold">
              R-{r.number} · {texts.returnStatus[r.status]}
            </p>
            {steps.length > 0 && (
              <div className="flex flex-wrap gap-2">
                {steps.map((step) => (
                  <Button key={step} variant={step === "reject" ? "danger" : "secondary"} size="sm" onClick={() => setOpen({ returnId: r.id, step })}>
                    {stepLabel[step]}
                  </Button>
                ))}
              </div>
            )}
            {open !== null && open !== "refund" && open.returnId === r.id && (
              <form onSubmit={(e) => answer(e, r.id, open.step)} noValidate className="flex flex-col gap-2">
                <label className="flex flex-col gap-1">
                  <span className="font-medium">{open.step === "receive" ? texts.orderOptionalNote : texts.returnAnswer}</span>
                  <textarea name="text" rows={2} maxLength={1000} className="rounded-xl border border-border bg-surface p-3" />
                </label>
                <div>
                  <Button type="submit" disabled={busy}>
                    {stepLabel[open.step]}
                  </Button>
                </div>
              </form>
            )}
          </div>
        );
      })}
      {order.actions.canRefund &&
        (open === "refund" ? (
          <form onSubmit={refund} noValidate className="grid gap-3 rounded-xl border border-border p-3 sm:grid-cols-2">
            <FormField
              label={`${texts.refundAmount} (${order.currency})`}
              name="amount"
              type="number"
              min={0}
              step="any"
              dir="ltr"
              required
              defaultValue={received.at(-1)?.suggestedRefund ?? order.refundable}
              hint={texts.refundAvailable.replace("{amount}", String(order.refundable))}
              error={fieldError("amount")}
            />
            <FormField label={texts.refundMethod} name="method" required maxLength={100} placeholder={texts.refundMethodHint} error={fieldError("method")} />
            {received.length > 0 && (
              <label className="flex flex-col gap-1 text-sm">
                <span className="font-medium">{texts.refundForReturn}</span>
                <select name="returnId" defaultValue={received.at(-1)?.id} className="h-12 rounded-xl border border-border bg-surface px-3">
                  <option value="">{texts.refundNoReturn}</option>
                  {received.map((r) => (
                    <option key={r.id} value={r.id}>
                      R-{r.number}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <FormField label={texts.refundNote} name="note" maxLength={500} />
            <div className="sm:col-span-2">
              <Button type="submit" disabled={busy}>
                {texts.refundRecord}
              </Button>
            </div>
          </form>
        ) : (
          <div>
            <Button variant="secondary" onClick={() => setOpen("refund")}>
              {texts.refundRecord}
            </Button>
          </div>
        ))}
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
    </section>
  );
}
