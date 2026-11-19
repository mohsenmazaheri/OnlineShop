"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, FormField, SelectField, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { displayName } from "./CatalogTreeEditor";
import { LocalizedFields, readLocalizedFields } from "./LocalizedFields";

type CatalogTree = components["schemas"]["CatalogTree"];
type ProductEditView = components["schemas"]["ProductEditView"];
type SpecChoice = components["schemas"]["SpecChoice"];
type BrandView = components["schemas"]["BrandView"];
export type SpecRow = components["schemas"]["ProductSpecInput"];
type Texts = Dictionary["admin"];

/** Every product group as "Category › Sub-category › Group", named in the admin's language. */
export function groupOptions(tree: CatalogTree, locale: string): { value: string; label: string }[] {
  return tree.categories.flatMap((c) =>
    c.subCategories.flatMap((s) =>
      s.groups.map((g) => ({ value: g.id, label: `${displayName(c.texts, locale)} › ${displayName(s.texts, locale)} › ${displayName(g.texts, locale)}` })),
    ),
  );
}

/** The template spec codes for a product group: its category's, then its sub-category's (each once). */
export function templateFor(tree: CatalogTree, groupId: string): string[] {
  for (const c of tree.categories)
    for (const s of c.subCategories) if (s.groups.some((g) => g.id === groupId)) return [...new Set([...c.templateSpecs, ...s.templateSpecs])];
  return [];
}

const emptyRow = (code: string): SpecRow => ({ code, number: null, boolean: null, text: null, optionCode: null });

/** The product's rows plus an empty row for every template spec it does not have yet (empty rows are not saved). */
export function withTemplate(rows: SpecRow[], template: string[], excluded: string[] = []): SpecRow[] {
  return [...rows, ...template.filter((code) => !excluded.includes(code) && !rows.some((r) => r.code === code)).map(emptyRow)];
}

/** A number typed with a comma or Persian/Arabic digits, or null for an empty field. */
export function parseNumber(value: FormDataEntryValue | null): number | null {
  if (typeof value !== "string" || !value.trim()) return null;
  const ascii = value.trim().replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d))).replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d)));
  const number = Number(ascii.replace(/[٬,](?=\d{3}\b)/g, "").replace(/[٫,]/, "."));
  return Number.isFinite(number) ? number : null;
}

function SpecValueInput({ row, spec, texts, onChange }: { row: SpecRow; spec: SpecChoice; texts: Texts; onChange: (row: SpecRow) => void }) {
  const base = "h-11 w-full rounded-xl border border-border bg-surface px-3";
  switch (spec.kind) {
    case "Number":
      return (
        <span className="flex items-center gap-2">
          <input
            aria-label={spec.name}
            inputMode="decimal"
            defaultValue={row.number ?? ""}
            onChange={(e) => onChange({ ...row, number: parseNumber(e.target.value) })}
            className={base}
            dir="ltr"
          />
          {spec.unit && <span className="text-sm text-muted">{spec.unit}</span>}
        </span>
      );
    case "Boolean":
      return (
        <select aria-label={spec.name} value={row.boolean == null ? "" : String(row.boolean)} onChange={(e) => onChange({ ...row, boolean: e.target.value === "" ? null : e.target.value === "true" })} className={base}>
          <option value="">—</option>
          <option value="true">{texts.yes}</option>
          <option value="false">{texts.no}</option>
        </select>
      );
    case "Option":
      return (
        <select aria-label={spec.name} value={row.optionCode ?? ""} onChange={(e) => onChange({ ...row, optionCode: e.target.value || null })} className={base}>
          <option value="">—</option>
          {spec.options.map((o) => (
            <option key={o.code} value={o.code}>
              {o.value}
            </option>
          ))}
        </select>
      );
    default:
      return <input aria-label={spec.name} defaultValue={row.text ?? ""} onChange={(e) => onChange({ ...row, text: e.target.value })} className={base} maxLength={500} />;
  }
}

type Props = {
  locale: string;
  texts: Texts;
  tree: CatalogTree;
  specs: SpecChoice[];
  brands: BrandView[];
  languageNames: Record<string, string>;
  /** The product being edited; absent for a new product. */
  product?: ProductEditView;
};

/** Create or edit a product: base data, texts per language (with "not translated" marks) and spec values. */
export function ProductEditor({ locale, texts, tree, specs, brands, languageNames, product }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.catalogErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);
  // Specs the variants vary by belong to each variant, not to the product.
  const variantSpecs = product?.variants.specCodes ?? [];
  const hasVariants = (product?.variants.variants.length ?? 0) > 0;
  const [rows, setRows] = useState<SpecRow[]>(() => withTemplate(product?.specs ?? [], product ? templateFor(tree, product.productGroupId) : [], variantSpecs));
  const groups = groupOptions(tree, locale);
  const unused = specs.filter((s) => !rows.some((r) => r.code === s.code) && !variantSpecs.includes(s.code));
  const brandOptions = brands.map((b) => ({ value: b.id, label: displayName(b.texts, locale) }));

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const form = new FormData(event.currentTarget);
    const body = {
      sku: String(form.get("sku") ?? "").trim(),
      price: parseNumber(form.get("price")) ?? -1,
      compareAtPrice: parseNumber(form.get("compareAtPrice")),
      stock: parseNumber(form.get("stock")) ?? -1,
      isPublished: form.get("isPublished") === "on",
      productGroupId: String(form.get("productGroupId") ?? ""),
      brandId: String(form.get("brandId") ?? "") || null,
      texts: readLocalizedFields(form, tree.locales),
      specs: rows.filter((r) => r.number != null || r.boolean != null || (r.text ?? "").trim() || r.optionCode),
    };
    const result = await submit<ProductEditView>(product ? `/api/v1/admin/products/${product.number}` : "/api/v1/admin/products", body, product ? "PUT" : "POST");
    if (!result.ok) return;
    await refreshStorefront(locale, ["catalog"]);
    if (product) {
      setSaved(true);
      router.refresh();
    } else {
      router.replace(`/${locale}/admin/products/${result.data.number}`);
    }
  }

  async function remove() {
    if (!product || !window.confirm(texts.confirmDeleteProduct)) return;
    const result = await submit(`/api/v1/admin/products/${product.number}`, undefined, "DELETE");
    if (result.ok) {
      await refreshStorefront(locale, ["catalog"]);
      router.replace(`/${locale}/admin/products`);
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-5">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.saved}</FormAlert>}

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <FormField label={texts.productSku} name="sku" defaultValue={product?.sku ?? ""} required maxLength={64} dir="ltr" />
        <FormField label={texts.productPrice} name="price" inputMode="decimal" defaultValue={product?.price ?? ""} required dir="ltr" />
        <FormField label={texts.productCompareAt} name="compareAtPrice" inputMode="decimal" defaultValue={product?.compareAtPrice ?? ""} hint={texts.productCompareAtHint} dir="ltr" />
        <FormField
          label={texts.productStock}
          name="stock"
          inputMode="numeric"
          defaultValue={product?.stock ?? 0}
          required
          dir="ltr"
          readOnly={hasVariants}
          hint={hasVariants ? texts.productStockFromVariants : undefined}
        />
      </div>
      <div className="grid items-end gap-3 sm:grid-cols-[2fr_1fr_auto]">
        <SelectField
          label={texts.productGroup}
          name="productGroupId"
          options={[{ value: "", label: "—" }, ...groups]}
          defaultValue={product?.productGroupId ?? ""}
          onChange={(e) => setRows((current) => withTemplate(current, templateFor(tree, e.target.value), variantSpecs))}
        />
        <SelectField label={texts.productBrand} name="brandId" options={[{ value: "", label: "—" }, ...brandOptions]} defaultValue={product?.brandId ?? ""} />
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" name="isPublished" defaultChecked={product?.isPublished ?? false} className="h-4 w-4" />
          {texts.productPublished}
        </label>
      </div>

      <LocalizedFields locales={tree.locales} defaultLocale={tree.defaultLocale} values={product?.texts ?? []} languageNames={languageNames} texts={texts} withSlug withDescription />

      <fieldset className="flex flex-col gap-3 rounded-xl border border-border p-3">
        <legend className="px-1 text-sm font-bold">{texts.productSpecs}</legend>
        {rows.length === 0 && <p className="text-sm text-muted">{texts.productNoSpecs}</p>}
        {rows.map((row, i) => {
          const spec = specs.find((s) => s.code === row.code);
          if (!spec) return null;
          return (
            <div key={row.code} className="grid items-center gap-2 sm:grid-cols-[minmax(8rem,1fr)_2fr_auto]">
              <span className="text-sm font-medium">{spec.name}</span>
              <SpecValueInput row={row} spec={spec} texts={texts} onChange={(next) => setRows(rows.map((r, j) => (j === i ? next : r)))} />
              <Button variant="ghost" size="sm" onClick={() => setRows(rows.filter((_, j) => j !== i))}>
                {texts.remove}
              </Button>
            </div>
          );
        })}
        {unused.length > 0 && (
          <select
            aria-label={texts.productAddSpec}
            value=""
            onChange={(e) => e.target.value && setRows([...rows, { code: e.target.value, number: null, boolean: null, text: null, optionCode: null }])}
            className="h-11 rounded-xl border border-dashed border-border bg-surface px-3 text-sm"
          >
            <option value="">+ {texts.productAddSpec}</option>
            {unused.map((s) => (
              <option key={s.code} value={s.code}>
                {s.groupName ? `${s.groupName} › ${s.name}` : s.name}
              </option>
            ))}
          </select>
        )}
      </fieldset>

      <div className="flex flex-wrap items-center gap-2">
        <SubmitButton busy={busy} label={product ? texts.save : texts.productCreate} busyLabel="…" />
        {product && (
          <>
            <Button variant="danger" onClick={remove} disabled={busy}>
              {texts.remove}
            </Button>
            {product.isPublished && (
              <Link href={`/${locale}/p/${product.number}`} className="text-sm text-primary underline">
                {texts.productView}
              </Link>
            )}
          </>
        )}
      </div>
    </form>
  );
}
