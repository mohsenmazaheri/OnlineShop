"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { OrderView } from "@/lib/account/session";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";

type Props = {
  order: OrderView;
  locale: string;
  /** The customer's endpoint, or a guest's with the order's key. */
  endpoint: `/api/v1/${string}`;
  texts: Dictionary["orders"];
  errors: ErrorTexts;
};

/**
 * The customer asks to send items back: how many of each line (up to what may still be returned) and why. Opens from a
 * button; after sending, the page reloads with the return in the list.
 */
export function ReturnRequestForm({ order, locale, endpoint, texts, errors }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm(errors);
  const [open, setOpen] = useState(false);
  const lines = order.lines.filter((l) => l.returnable > 0);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const chosen = lines.map((l) => ({ orderItemId: l.id, quantity: Number(form.get(`qty-${l.id}`) ?? 0) })).filter((l) => l.quantity > 0);
    const result = await submit(endpoint, { lines: chosen, reason: String(form.get("reason") ?? "").trim() });
    if (!result.ok) return;
    setOpen(false);
    router.refresh();
  }

  return (
    <section aria-labelledby="return-request" className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
      <h2 id="return-request" className="font-bold">
        {texts.returnTitle}
      </h2>
      {order.returnableUntilUtc && <p className="text-sm text-muted">{texts.returnUntil.replace("{date}", formatDate(order.returnableUntilUtc, locale))}</p>}
      {!open ? (
        <div>
          <Button variant="secondary" onClick={() => setOpen(true)}>
            {texts.returnStart}
          </Button>
        </div>
      ) : (
        <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
          <ul className="flex flex-col gap-2">
            {lines.map((l) => (
              <li key={l.id} className="flex items-center justify-between gap-3 text-sm">
                <label htmlFor={`qty-${l.id}`} className="min-w-0">
                  {l.name}
                  {l.variantLabel && <span className="block text-xs text-muted">{l.variantLabel}</span>}
                </label>
                <select id={`qty-${l.id}`} name={`qty-${l.id}`} defaultValue="0" className="h-10 rounded-xl border border-border bg-surface px-2">
                  {Array.from({ length: l.returnable + 1 }, (_, n) => (
                    <option key={n} value={n}>
                      {new Intl.NumberFormat(locale).format(n)}
                    </option>
                  ))}
                </select>
              </li>
            ))}
          </ul>
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium">{texts.returnReason}</span>
            <textarea name="reason" rows={3} maxLength={1000} required className="rounded-xl border border-border bg-surface p-3" />
          </label>
          {formError && <FormAlert kind="error">{formError}</FormAlert>}
          <div className="flex gap-2">
            <Button type="submit" disabled={busy}>
              {texts.returnSend}
            </Button>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              {texts.returnCancel}
            </Button>
          </div>
        </form>
      )}
    </section>
  );
}
