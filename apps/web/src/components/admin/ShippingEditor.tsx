"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { ShippingMethodAdmin, TaxSettings } from "@/lib/account/session";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { regionName } from "@/lib/i18n/display-names";
import { LocalizedFields, readLocalizedFields } from "./LocalizedFields";

type Texts = Dictionary["admin"];

type Props = {
  locale: string;
  texts: Texts;
  methods: ShippingMethodAdmin[];
  tax: TaxSettings;
  /** Shop languages, the default first. */
  locales: string[];
  defaultLocale: string;
  countries: string[];
  currency: string;
  languageNames: Record<string, string>;
};

/** Reads an optional number field ("" → null). */
export function optionalNumber(form: FormData, name: string): number | null {
  const value = String(form.get(name) ?? "").trim();
  return value === "" ? null : Number(value);
}

/** The tax rate and the shipping methods (settings.manage). Amounts are in the base currency, as prices are. */
export function ShippingEditor({ locale, texts, methods, tax, locales, defaultLocale, countries, currency, languageNames }: Props) {
  const [open, setOpen] = useState<string | null>(methods.length === 0 ? "new" : null);
  const form = { locale, texts, locales, defaultLocale, countries, currency, languageNames };
  return (
    <div className="flex flex-col gap-6">
      <TaxForm texts={texts} tax={tax} />
      <section aria-labelledby="shipping-methods" className="flex flex-col gap-3">
        <h2 id="shipping-methods" className="font-bold">
          {texts.methodsTitle}
        </h2>
        {methods.length === 0 && <p className="text-sm text-muted">{texts.methodEmpty}</p>}
        <ul className="flex flex-col gap-2">
          {methods.map((method) => (
            <li key={method.id} className="rounded-xl border border-border">
              <button
                type="button"
                aria-expanded={open === method.id}
                onClick={() => setOpen(open === method.id ? null : method.id)}
                className="flex w-full items-center justify-between gap-2 px-3 py-3 text-start text-sm"
              >
                <span className="font-medium">{method.texts[0]?.name}</span>
                <span className="text-xs text-muted">
                  {method.isActive ? (method.isPickup ? texts.methodPickup : method.countries.map((c) => regionName(c, locale)).join("، ") || texts.methodAllCountries) : texts.methodInactive}
                </span>
              </button>
              {open === method.id && (
                <div className="border-t border-border p-3">
                  <MethodForm {...form} method={method} onDone={() => setOpen(null)} />
                </div>
              )}
            </li>
          ))}
        </ul>
        {open === "new" ? (
          <div className="rounded-xl border border-border p-3">
            <MethodForm {...form} onDone={() => setOpen(null)} />
          </div>
        ) : (
          <div>
            <Button variant="secondary" onClick={() => setOpen("new")}>
              {texts.methodNew}
            </Button>
          </div>
        )}
      </section>
    </div>
  );
}

function TaxForm({ texts, tax }: { texts: Texts; tax: TaxSettings }) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm({ ...texts.errors, ...texts.shippingErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const form = new FormData(event.currentTarget);
    const result = await submit("/api/v1/admin/shipping/tax", { rate: optionalNumber(form, "rate") ?? 0, pricesIncludeTax: form.get("pricesIncludeTax") === "on" }, "PUT");
    if (!result.ok) return;
    setSaved(true);
    router.refresh();
  }

  return (
    <form onSubmit={save} noValidate aria-labelledby="tax-title" className="flex flex-col gap-3 rounded-xl border border-border p-3">
      <h2 id="tax-title" className="font-bold">
        {texts.taxTitle}
      </h2>
      <p className="text-sm text-muted">{texts.taxIntro}</p>
      <div className="max-w-48">
        <FormField label={texts.taxRate} name="rate" type="number" min={0} max={100} step={0.01} dir="ltr" defaultValue={tax.rate} error={fieldError("rate")} />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="pricesIncludeTax" defaultChecked={tax.pricesIncludeTax} className="h-4 w-4" />
        {texts.taxIncluded}
      </label>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.taxSaved}</FormAlert>}
      <div>
        <Button type="submit" disabled={busy}>
          {texts.taxSave}
        </Button>
      </div>
    </form>
  );
}

type FormProps = Omit<Props, "methods" | "tax"> & { method?: ShippingMethodAdmin; onDone: () => void };

function MethodForm({ texts, locales, defaultLocale, countries, currency, languageNames, method, onDone, locale }: FormProps) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm({ ...texts.errors, ...texts.catalogErrors, ...texts.shippingErrors } as ErrorTexts);
  const [pickup, setPickup] = useState(method?.isPickup ?? false);
  const prefix = method ? `${method.id}-` : "new-";

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = {
      texts: readLocalizedFields(form, locales, prefix),
      isPickup: pickup,
      cost: optionalNumber(form, "cost") ?? 0,
      costPerKg: optionalNumber(form, "costPerKg") ?? 0,
      freeOver: optionalNumber(form, "freeOver"),
      countries: form.getAll("countries").map(String),
      minDays: optionalNumber(form, "minDays"),
      maxDays: optionalNumber(form, "maxDays"),
      isActive: form.get("isActive") === "on",
      sortOrder: optionalNumber(form, "sortOrder") ?? 0,
    };
    const result = await submit(method ? `/api/v1/admin/shipping/${method.id}` : "/api/v1/admin/shipping", body, method ? "PUT" : "POST");
    if (!result.ok) return;
    onDone();
    router.refresh();
  }

  async function remove() {
    if (!method || !window.confirm(texts.methodConfirmDelete)) return;
    const result = await submit(`/api/v1/admin/shipping/${method.id}`, undefined, "DELETE");
    if (result.ok) router.refresh();
  }

  const amount = (label: string) => `${label} (${currency})`;
  return (
    <form onSubmit={save} noValidate className="flex flex-col gap-3">
      <LocalizedFields locales={locales} defaultLocale={defaultLocale} values={method?.texts ?? []} languageNames={languageNames} texts={texts} withDescription prefix={prefix} />
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={pickup} onChange={(e) => setPickup(e.target.checked)} className="h-4 w-4" />
        {texts.methodPickupLabel}
      </label>
      <div className="grid gap-3 sm:grid-cols-3">
        <FormField label={amount(texts.methodCost)} name="cost" id={`${prefix}cost`} type="number" min={0} step="any" dir="ltr" defaultValue={method?.cost ?? 0} error={fieldError("cost")} />
        {!pickup && (
          <FormField label={amount(texts.methodCostPerKg)} name="costPerKg" id={`${prefix}per-kg`} type="number" min={0} step="any" dir="ltr" defaultValue={method?.costPerKg ?? 0} error={fieldError("costPerKg")} />
        )}
        <FormField label={amount(texts.methodFreeOver)} name="freeOver" id={`${prefix}free-over`} type="number" min={0} step="any" dir="ltr" defaultValue={method?.freeOver ?? ""} hint={texts.methodFreeOverHint} error={fieldError("freeOver")} />
        <FormField label={texts.methodMinDays} name="minDays" id={`${prefix}min-days`} type="number" min={0} max={365} dir="ltr" defaultValue={method?.minDays ?? ""} error={fieldError("minDays")} />
        <FormField label={texts.methodMaxDays} name="maxDays" id={`${prefix}max-days`} type="number" min={0} max={365} dir="ltr" defaultValue={method?.maxDays ?? ""} error={fieldError("maxDays")} />
        <FormField label={texts.methodSortOrder} name="sortOrder" id={`${prefix}sort`} type="number" min={0} dir="ltr" defaultValue={method?.sortOrder ?? 0} />
      </div>
      {!pickup && countries.length > 1 && (
        <fieldset className="flex flex-col gap-2 text-sm">
          <legend className="font-medium">{texts.methodCountries}</legend>
          <p className="text-xs text-muted">{texts.methodCountriesHint}</p>
          <div className="flex flex-wrap gap-3">
            {countries.map((c) => (
              <label key={c} className="flex items-center gap-2">
                <input type="checkbox" name="countries" value={c} defaultChecked={method?.countries.includes(c)} className="h-4 w-4" />
                {regionName(c, locale)}
              </label>
            ))}
          </div>
        </fieldset>
      )}
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="isActive" defaultChecked={method?.isActive ?? true} className="h-4 w-4" />
        {texts.methodActive}
      </label>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={busy}>
          {texts.methodSave}
        </Button>
        {method && (
          <Button variant="ghost" onClick={remove} disabled={busy} className="text-danger">
            {texts.methodDelete}
          </Button>
        )}
      </div>
    </form>
  );
}
