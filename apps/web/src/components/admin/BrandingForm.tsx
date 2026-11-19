"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import type { ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import type { Branding } from "@/lib/store/settings";

type Texts = Dictionary["admin"];

function Choice<T extends string>({ name, legend, value, options }: { name: string; legend: string; value: T; options: { value: T; label: string }[] }) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="mb-1 text-sm font-bold">{legend}</legend>
      {options.map((option) => (
        <label key={option.value} className="flex min-h-11 items-center gap-2 rounded-xl border border-border px-3 text-sm has-[:checked]:border-primary">
          <input type="radio" name={name} value={option.value} defaultChecked={option.value === value} className="h-4 w-4" />
          {option.label}
        </label>
      ))}
    </fieldset>
  );
}

/** Main colour, corners, font and home layout; the storefront applies them on the next page load. */
export function BrandingForm({ locale, texts, branding }: { locale: string; texts: Texts; branding: Branding }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.brandingErrors } as ErrorTexts);
  const [color, setColor] = useState(branding.primaryColor);
  const [saved, setSaved] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const form = new FormData(event.currentTarget);
    const result = await submit(
      "/api/v1/admin/branding",
      { primaryColor: color, corners: form.get("corners"), font: form.get("font"), homeLayout: form.get("homeLayout") },
      "PUT",
    );
    if (result.ok) {
      setSaved(true);
      await refreshStorefront(locale, ["store"]);
      router.refresh();
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-5">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.saved}</FormAlert>}

      <div className="flex flex-col gap-2">
        <label htmlFor="brand-color" className="text-sm font-bold">
          {texts.brandColor}
        </label>
        <div className="flex items-center gap-3">
          <input id="brand-color" type="color" value={color} onChange={(e) => setColor(e.target.value)} className="h-11 w-16 cursor-pointer rounded-lg border border-border" />
          <input
            aria-label={texts.brandColor}
            value={color}
            onChange={(e) => setColor(e.target.value.trim())}
            dir="ltr"
            maxLength={7}
            className="h-11 w-32 rounded-xl border border-border bg-surface px-3 font-mono text-sm"
          />
          <span aria-hidden className="rounded-xl px-4 py-2 text-sm font-bold text-white" style={{ background: /^#[0-9a-f]{6}$/i.test(color) ? color : undefined }}>
            Aa
          </span>
        </div>
      </div>

      <div className="grid gap-5 md:grid-cols-2">
        <Choice
          name="corners"
          legend={texts.corners}
          value={branding.corners}
          options={[
            { value: "Sharp", label: texts.cornersSharp },
            { value: "Soft", label: texts.cornersSoft },
            { value: "Round", label: texts.cornersRound },
          ]}
        />
        <Choice
          name="font"
          legend={texts.font}
          value={branding.font}
          options={[
            { value: "Vazirmatn", label: texts.fontVazirmatn },
            { value: "System", label: texts.fontSystem },
          ]}
        />
      </div>

      <Choice
        name="homeLayout"
        legend={texts.homeLayout}
        value={branding.homeLayout}
        options={[
          { value: "Classic", label: texts.layoutClassic },
          { value: "Catalog", label: texts.layoutCatalog },
          { value: "Showcase", label: texts.layoutShowcase },
        ]}
      />

      <div>
        <SubmitButton busy={busy} label={texts.save} busyLabel="…" />
      </div>
    </form>
  );
}
