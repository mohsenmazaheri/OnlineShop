"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField, SelectField } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { DiscountCodeAdmin } from "@/lib/account/session";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { optionalNumber } from "./ShippingEditor";

type Texts = Dictionary["admin"];
type Kind = DiscountCodeAdmin["kind"];

type Props = { locale: string; texts: Texts; codes: DiscountCodeAdmin[]; currency: string };

/** "2026-03-01" from a stored time (UTC, so the server and the browser show the same day). */
export function dayOf(utc: string | null): string {
  return utc ? utc.slice(0, 10) : "";
}

/** A day from a date field as a UTC time: its start, or for the last day its end (the code works all that day). */
export function utcOf(day: string, end = false): string | null {
  return /^\d{4}-\d{2}-\d{2}$/.test(day) ? `${day}T${end ? "23:59:59" : "00:00:00"}Z` : null;
}

/** What a code gives, in a few words: "10 % (at most 5 EUR)", "5 EUR", "free shipping". */
export function discountSummary(code: Pick<DiscountCodeAdmin, "kind" | "value" | "maxDiscount">, currency: string, locale: string, texts: Texts): string {
  const number = (n: number) => new Intl.NumberFormat(locale).format(n);
  if (code.kind === "FreeShipping") return texts.discountKindFreeShipping;
  if (code.kind === "Amount") return `${number(code.value)} ${currency}`;
  const percent = `${number(code.value)}%`;
  return code.maxDiscount === null ? percent : `${percent} (${texts.discountAtMost.replace("{amount}", `${number(code.maxDiscount)} ${currency}`)})`;
}

/** The shop's discount codes (discounts.manage): each opens into its form; a new one below. */
export function DiscountsEditor({ locale, texts, codes, currency }: Props) {
  const [open, setOpen] = useState<string | null>(codes.length === 0 ? "new" : null);
  return (
    <div className="flex flex-col gap-3">
      {codes.length === 0 && <p className="text-sm text-muted">{texts.discountEmpty}</p>}
      <ul className="flex flex-col gap-2">
        {codes.map((code) => (
          <li key={code.id} className="rounded-xl border border-border">
            <button
              type="button"
              aria-expanded={open === code.id}
              onClick={() => setOpen(open === code.id ? null : code.id)}
              className="flex w-full flex-wrap items-center justify-between gap-2 px-3 py-3 text-start text-sm"
            >
              <span className="font-mono font-bold" dir="ltr">
                {code.code}
              </span>
              <span className="text-xs text-muted">
                {code.isActive ? discountSummary(code, currency, locale, texts) : texts.discountInactive} ·{" "}
                {(code.usageLimit === null ? texts.discountUsed : texts.discountUsedOf)
                  .replace("{used}", new Intl.NumberFormat(locale).format(code.usedCount))
                  .replace("{limit}", new Intl.NumberFormat(locale).format(code.usageLimit ?? 0))}
              </span>
            </button>
            {open === code.id && (
              <div className="border-t border-border p-3">
                <DiscountForm texts={texts} currency={currency} code={code} onDone={() => setOpen(null)} />
              </div>
            )}
          </li>
        ))}
      </ul>
      {open === "new" ? (
        <div className="rounded-xl border border-border p-3">
          <DiscountForm texts={texts} currency={currency} onDone={() => setOpen(null)} />
        </div>
      ) : (
        <div>
          <Button variant="secondary" onClick={() => setOpen("new")}>
            {texts.discountNew}
          </Button>
        </div>
      )}
    </div>
  );
}

function DiscountForm({ texts, currency, code, onDone }: { texts: Texts; currency: string; code?: DiscountCodeAdmin; onDone: () => void }) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm({ ...texts.errors, ...texts.discountErrors } as ErrorTexts);
  const [kind, setKind] = useState<Kind>(code?.kind ?? "Percent");
  const prefix = code ? `${code.id}-` : "new-";

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = {
      code: String(form.get("code") ?? "").trim(),
      kind,
      value: kind === "FreeShipping" ? 0 : (optionalNumber(form, "value") ?? 0),
      minSubtotal: optionalNumber(form, "minSubtotal"),
      maxDiscount: kind === "Percent" ? optionalNumber(form, "maxDiscount") : null,
      startsUtc: utcOf(String(form.get("starts") ?? "")),
      endsUtc: utcOf(String(form.get("ends") ?? ""), true),
      usageLimit: optionalNumber(form, "usageLimit"),
      oncePerCustomer: form.get("oncePerCustomer") === "on",
      isActive: form.get("isActive") === "on",
      note: String(form.get("note") ?? "").trim() || null,
    };
    const result = await submit(code ? `/api/v1/admin/discounts/${code.id}` : "/api/v1/admin/discounts", body, code ? "PUT" : "POST");
    if (!result.ok) return;
    onDone();
    router.refresh();
  }

  async function remove() {
    if (!code || !window.confirm(texts.discountConfirmDelete)) return;
    const result = await submit(`/api/v1/admin/discounts/${code.id}`, undefined, "DELETE");
    if (result.ok) router.refresh();
  }

  const amount = (label: string) => `${label} (${currency})`;
  return (
    <form onSubmit={save} noValidate className="flex flex-col gap-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <FormField label={texts.discountCode} name="code" id={`${prefix}code`} dir="ltr" required maxLength={40} defaultValue={code?.code ?? ""} hint={texts.discountCodeHint} error={fieldError("code")} className="uppercase" />
        <SelectField
          label={texts.discountKind}
          name="kind"
          id={`${prefix}kind`}
          value={kind}
          onChange={(e) => setKind(e.target.value as Kind)}
          options={[
            { value: "Percent", label: texts.discountKindPercent },
            { value: "Amount", label: texts.discountKindAmount },
            { value: "FreeShipping", label: texts.discountKindFreeShipping },
          ]}
        />
        {kind !== "FreeShipping" && (
          <FormField
            label={kind === "Percent" ? texts.discountPercent : amount(texts.discountAmount)}
            name="value"
            id={`${prefix}value`}
            type="number"
            min={0}
            step="any"
            dir="ltr"
            required
            defaultValue={code?.value ?? ""}
            error={fieldError("value")}
          />
        )}
        {kind === "Percent" && (
          <FormField label={amount(texts.discountMaxDiscount)} name="maxDiscount" id={`${prefix}max`} type="number" min={0} step="any" dir="ltr" defaultValue={code?.maxDiscount ?? ""} error={fieldError("maxDiscount")} />
        )}
        <FormField label={amount(texts.discountMinSubtotal)} name="minSubtotal" id={`${prefix}min`} type="number" min={0} step="any" dir="ltr" defaultValue={code?.minSubtotal ?? ""} error={fieldError("minSubtotal")} />
        <FormField label={texts.discountUsageLimit} name="usageLimit" id={`${prefix}limit`} type="number" min={1} dir="ltr" defaultValue={code?.usageLimit ?? ""} hint={texts.discountUsageLimitHint} error={fieldError("usageLimit")} />
        <FormField label={texts.discountStarts} name="starts" id={`${prefix}starts`} type="date" dir="ltr" defaultValue={dayOf(code?.startsUtc ?? null)} hint={texts.discountDatesHint} />
        <FormField label={texts.discountEnds} name="ends" id={`${prefix}ends`} type="date" dir="ltr" defaultValue={dayOf(code?.endsUtc ?? null)} error={fieldError("endsUtc")} />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="oncePerCustomer" defaultChecked={code?.oncePerCustomer ?? false} className="h-4 w-4" />
        {texts.discountOnce}
      </label>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="isActive" defaultChecked={code?.isActive ?? true} className="h-4 w-4" />
        {texts.discountActive}
      </label>
      <FormField label={texts.discountNote} name="note" id={`${prefix}note`} maxLength={300} defaultValue={code?.note ?? ""} />
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={busy}>
          {texts.discountSave}
        </Button>
        {code && (
          <Button variant="ghost" onClick={remove} disabled={busy} className="text-danger">
            {texts.discountDelete}
          </Button>
        )}
      </div>
    </form>
  );
}
