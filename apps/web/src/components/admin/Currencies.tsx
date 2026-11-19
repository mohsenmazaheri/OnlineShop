"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField, SelectField, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import { formatDate } from "@/lib/i18n/format-date";
import type { Dictionary } from "@/lib/i18n/dictionaries";

export type CurrencyView = components["schemas"]["CurrencyView"];
export type CurrencyAdminOverview = components["schemas"]["CurrencyAdminOverview"];
type Texts = Dictionary["admin"];

/** Admin error texts plus the currency codes (currency.* lives in its own dictionary section). */
export function currencyErrorTexts(texts: Texts): ErrorTexts {
  return { ...texts.errors, ...texts.currencyErrors } as ErrorTexts;
}

/** The request body from a currency form (numbers parsed; the code only matters when adding). */
export function readCurrencyForm(form: FormData, code: string) {
  const number = (name: string) => Number(String(form.get(name) ?? "").replace(",", "."));
  return {
    code,
    symbol: String(form.get("symbol") ?? "").trim() || null,
    decimals: number("decimals"),
    roundingStep: number("roundingStep"),
    rate: number("rate"),
    autoUpdate: form.get("autoUpdate") === "on",
    isEnabled: form.get("isEnabled") === "on",
    sortOrder: number("sortOrder"),
  };
}

function CurrencyFields({ texts, base, currency }: { texts: Texts; base: string; currency?: CurrencyView }) {
  const isBase = currency?.isBase ?? false;
  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <FormField label={texts.currencySymbol} name="symbol" defaultValue={currency?.symbol ?? ""} hint={texts.currencySymbolHint} maxLength={12} />
      <FormField label={texts.currencyDecimals} name="decimals" type="number" min={0} max={4} defaultValue={currency?.decimals ?? 2} dir="ltr" />
      <FormField label={texts.currencyStep} name="roundingStep" inputMode="decimal" defaultValue={currency?.roundingStep ?? 0.01} dir="ltr" />
      <FormField
        label={texts.currencyRate.replace("{base}", base)}
        name="rate"
        inputMode="decimal"
        defaultValue={currency?.rate ?? 0}
        disabled={isBase}
        dir="ltr"
      />
      <FormField label={texts.currencyOrder} name="sortOrder" type="number" defaultValue={currency?.sortOrder ?? 10} dir="ltr" />
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="autoUpdate" defaultChecked={currency?.autoUpdate ?? true} disabled={isBase} className="h-4 w-4" />
        {texts.currencyAuto}
      </label>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="isEnabled" defaultChecked={currency?.isEnabled ?? true} disabled={isBase} className="h-4 w-4" />
        {texts.currencyEnabled}
      </label>
    </div>
  );
}

/** One currency: its settings in a form of its own, with the time its rate last changed. */
export function CurrencyForm({ locale, texts, base, currency }: { locale: string; texts: Texts; base: string; currency: CurrencyView }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm(currencyErrorTexts(texts));
  const [saved, setSaved] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const body = readCurrencyForm(new FormData(event.currentTarget), currency.code);
    // Disabled inputs are not sent: the base currency keeps its fixed values.
    if (currency.isBase) Object.assign(body, { rate: 1, autoUpdate: false, isEnabled: true });
    const result = await submit(`/api/v1/admin/currencies/${currency.code}`, body, "PUT");
    if (result.ok) {
      setSaved(true);
      await refreshStorefront(locale, ["currencies"]);
      router.refresh();
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate aria-label={currency.code} className="flex flex-col gap-3 border-b border-border pb-4 last:border-0">
      <div className="flex flex-wrap items-baseline gap-2">
        <strong dir="ltr" className="text-lg">
          {currency.code}
        </strong>
        {currency.isBase && <span className="rounded-md bg-surface-muted px-2 text-xs">{texts.currencyBase}</span>}
        <span className="text-xs text-muted">
          {currency.rate > 0
            ? currency.rateUpdatedUtc
              ? texts.currencyUpdated.replace("{date}", formatDate(currency.rateUpdatedUtc, locale, true))
              : ""
            : texts.currencyNoRate}
        </span>
      </div>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.saved}</FormAlert>}
      <CurrencyFields texts={texts} base={base} currency={currency} />
      <div>
        <SubmitButton busy={busy} label={texts.save} busyLabel="…" />
      </div>
    </form>
  );
}

/** Add a currency: an ISO code (GBP) or a shop-defined one such as IRT for toman (with its symbol and a rate). */
export function AddCurrencyForm({ locale, texts, base }: { locale: string; texts: Texts; base: string }) {
  const router = useRouter();
  const { busy, submit, formError, fieldError } = useApiForm(currencyErrorTexts(texts));

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const result = await submit("/api/v1/admin/currencies", readCurrencyForm(form, String(form.get("code") ?? "").trim().toUpperCase()));
    if (result.ok) {
      element.reset();
      await refreshStorefront(locale, ["currencies"]);
      router.refresh();
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <FormField label={texts.currencyCode} name="code" maxLength={3} required dir="ltr" autoComplete="off" error={fieldError("code")} />
      <CurrencyFields texts={texts} base={base} />
      <div>
        <SubmitButton busy={busy} label={texts.addCurrency} busyLabel="…" />
      </div>
    </form>
  );
}

/** Which currency each language starts with (one select per language, saved on change). */
export function LocaleDefaultsForm({
  locale,
  texts,
  defaults,
  currencies,
  languageNames,
}: {
  locale: string;
  texts: Texts;
  defaults: { locale: string; currencyCode: string }[];
  currencies: CurrencyView[];
  languageNames: Record<string, string>;
}) {
  const router = useRouter();
  const { submit, formError } = useApiForm(currencyErrorTexts(texts));
  const options = currencies.filter((c) => c.isEnabled).map((c) => ({ value: c.code, label: c.symbol ? `${c.code} (${c.symbol})` : c.code }));

  async function change(language: string, currencyCode: string) {
    const result = await submit(`/api/v1/admin/currencies/locales/${language}`, { currencyCode }, "PUT");
    if (result.ok) {
      await refreshStorefront(locale, ["currencies"]);
      router.refresh();
    }
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-muted">{texts.localeDefaultsHint}</p>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <div className="grid gap-3 sm:grid-cols-2">
        {defaults.map((d) => (
          <SelectField
            key={d.locale}
            label={languageNames[d.locale] ?? d.locale}
            name={`default-${d.locale}`}
            options={options}
            defaultValue={d.currencyCode}
            onChange={(e) => change(d.locale, e.target.value)}
          />
        ))}
      </div>
    </div>
  );
}

/** Where automatic rates come from, and a button to fetch them now. */
export function SyncRatesPanel({ locale, texts, source }: { locale: string; texts: Texts; source: string }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm(currencyErrorTexts(texts));
  const [updated, setUpdated] = useState<number | null>(null);
  const off = source === "None";

  async function sync() {
    setUpdated(null);
    const result = await submit<{ updated: number }>("/api/v1/admin/currencies/sync", {});
    if (result.ok) {
      setUpdated(result.data.updated);
      await refreshStorefront(locale, ["currencies"]);
      router.refresh();
    }
  }

  return (
    <div className="flex flex-col gap-3 text-sm">
      <p>{off ? texts.syncOff : texts.syncSource.replace("{source}", source)}</p>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {updated !== null && <FormAlert kind="success">{texts.synced.replace("{count}", String(updated))}</FormAlert>}
      {!off && (
        <div>
          <Button variant="secondary" onClick={sync} disabled={busy}>
            {texts.syncNow}
          </Button>
        </div>
      )}
    </div>
  );
}
