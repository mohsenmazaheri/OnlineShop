"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { displayName } from "./CatalogTreeEditor";
import { ImageUploadField } from "./ImageUploadField";
import { LocalizedFields, readLocalizedFields, type LocalizedText } from "./LocalizedFields";

type BrandList = components["schemas"]["BrandList"];
type BrandView = components["schemas"]["BrandView"];
type Texts = Dictionary["admin"];

type Shared = { locale: string; texts: Texts; list: BrandList; languageNames: Record<string, string> };

function BrandForm({ brand, ...shared }: Shared & { brand?: BrandView }) {
  const { locale, texts, list, languageNames } = shared;
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.catalogErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);
  const prefix = `brand-${brand?.id ?? "new"}-`;
  const empty: LocalizedText[] = [];

  async function done() {
    await refreshStorefront(locale, ["catalog"]);
    router.refresh();
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const element = event.currentTarget;
    const body = { texts: readLocalizedFields(new FormData(element), list.locales, prefix) };
    const result = await submit(brand ? `/api/v1/admin/brands/${brand.id}` : "/api/v1/admin/brands", body, brand ? "PUT" : "POST");
    if (!result.ok) return;
    setSaved(true);
    if (!brand) element.reset();
    await done();
  }

  async function remove() {
    if (!brand || !window.confirm(texts.confirmDeleteBrand)) return;
    const result = await submit(`/api/v1/admin/brands/${brand.id}`, undefined, "DELETE");
    if (result.ok) await done();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && brand && <FormAlert kind="success">{texts.saved}</FormAlert>}
      <LocalizedFields
        locales={list.locales}
        defaultLocale={list.defaultLocale}
        values={brand?.texts ?? empty}
        languageNames={languageNames}
        texts={texts}
        withSlug
        withDescription
        prefix={prefix}
      />
      <div className="flex flex-wrap gap-2">
        <SubmitButton busy={busy} label={brand ? texts.save : texts.add} busyLabel="…" />
        {brand && (
          <Button variant="danger" onClick={remove} disabled={busy || brand.productCount > 0} title={brand.productCount > 0 ? texts.brandInUse : undefined}>
            {texts.remove}
          </Button>
        )}
      </div>
    </form>
  );
}

/** The shop's brands: each opens to its texts per language, its logo and delete (only without products); plus "add". */
export function BrandsEditor(shared: Shared) {
  const { locale, texts, list } = shared;
  return (
    <div className="flex flex-col gap-3">
      {list.brands.length === 0 && <p className="text-sm text-muted">{texts.brandsEmpty}</p>}
      {list.brands.map((brand) => (
        <details key={brand.id} className="rounded-xl border border-border bg-surface p-3 open:shadow-sm">
          <summary className="flex cursor-pointer items-center justify-between gap-2 font-medium">
            <span>{displayName(brand.texts, locale)}</span>
            <span className="text-xs text-muted">{texts.productCount.replace("{count}", String(brand.productCount))}</span>
          </summary>
          <div className="mt-3 flex flex-col gap-3">
            <BrandForm {...shared} brand={brand} />
            <ImageUploadField
              locale={locale}
              label={texts.brandLogo}
              currentUrl={brand.logoUrl}
              uploadPath={`/api/v1/admin/brands/${brand.id}/logo`}
              removePath={`/api/v1/admin/brands/${brand.id}/logo`}
              texts={texts}
              errorTexts={{ ...texts.errors, ...texts.catalogErrors, ...texts.mediaErrors } as ErrorTexts}
              refresh={["catalog"]}
            />
          </div>
        </details>
      ))}
      <details className="rounded-xl border border-dashed border-border bg-surface p-3">
        <summary className="cursor-pointer font-medium">+ {texts.addBrand}</summary>
        <div className="mt-3">
          <BrandForm {...shared} />
        </div>
      </details>
    </div>
  );
}
