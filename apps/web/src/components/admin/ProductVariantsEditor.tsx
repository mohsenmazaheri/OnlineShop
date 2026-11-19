"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { parseNumber } from "./ProductEditor";

type SpecChoice = components["schemas"]["SpecChoice"];
type VariantsView = components["schemas"]["ProductVariantsView"];
type Choice = components["schemas"]["VariantOptionInput"];
type Texts = Dictionary["admin"];

export const maxVariantSpecs = 3;

/** A variant row as edited: numbers stay text until saving (Persian digits, commas). */
export type VariantRow = { id: string | null; sku: string; price: string; compareAtPrice: string; stock: string; options: Choice[] };

const key = (options: Choice[]) =>
  options
    .map((o) => `${o.spec}=${o.option}`)
    .sort()
    .join("|");

/** Every combination of the chosen options, one option per spec, in spec order. */
export function combinations(axes: { spec: string; options: string[] }[]): Choice[][] {
  return axes.reduce<Choice[][]>((acc, axis) => acc.flatMap((combo) => axis.options.map((option) => [...combo, { spec: axis.spec, option }])), [[]]).filter(
    (combo) => combo.length === axes.length && axes.length > 0,
  );
}

/** A suggested SKU: the product's SKU and the option codes ("TEE-1-BEIGE-M"). */
export function variantSku(productSku: string, options: Choice[]): string {
  return [productSku, ...options.map((o) => o.option)].join("-").toUpperCase().slice(0, 64);
}

/** The rows for the chosen options: existing rows keep their id, SKU, prices and stock; new combinations start at 0. */
export function buildVariants(axes: { spec: string; options: string[] }[], rows: VariantRow[], productSku: string): VariantRow[] {
  const existing = new Map(rows.map((r) => [key(r.options), r]));
  return combinations(axes).map(
    (options) => existing.get(key(options)) ?? { id: null, sku: variantSku(productSku, options), price: "", compareAtPrice: "", stock: "0", options },
  );
}

/** The request body; prices left empty mean "the product's price". */
export function toVariantsRequest(specCodes: string[], rows: VariantRow[]) {
  return {
    specCodes,
    variants: rows.map((r) => ({
      id: r.id,
      sku: r.sku.trim(),
      price: parseNumber(r.price),
      compareAtPrice: parseNumber(r.compareAtPrice),
      stock: parseNumber(r.stock) ?? -1,
      options: r.options,
    })),
  };
}

type Props = {
  locale: string;
  number: number;
  productSku: string;
  /** Option specs (the only kind a product can vary by). */
  specs: SpecChoice[];
  /** Specs the product has its own value for: they cannot be variant specs. */
  productSpecCodes: string[];
  view: VariantsView;
  texts: Texts;
};

/**
 * A product's variants: choose up to three option specs (colour, size) and their options, build the combinations,
 * then give each variant its SKU, stock and optionally its own price. Saved as one set.
 */
export function ProductVariantsEditor({ locale, number, productSku, specs, productSpecCodes, view, texts }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.catalogErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);
  const [axes, setAxes] = useState<string[]>(view.specCodes);
  const [chosen, setChosen] = useState<Record<string, string[]>>(() =>
    Object.fromEntries(view.specCodes.map((code) => [code, [...new Set(view.variants.map((v) => v.options.find((o) => o.spec === code)?.option ?? ""))].filter(Boolean)])),
  );
  const [rows, setRows] = useState<VariantRow[]>(() =>
    view.variants.map((v) => ({
      id: v.id,
      sku: v.sku,
      price: v.price == null ? "" : String(v.price),
      compareAtPrice: v.compareAtPrice == null ? "" : String(v.compareAtPrice),
      stock: String(v.stock),
      options: v.options,
    })),
  );
  const optionSpecs = specs.filter((s) => s.kind === "Option");
  const ordered = optionSpecs.filter((s) => axes.includes(s.code));
  const label = (options: Choice[]) =>
    options.map((o) => optionSpecs.find((s) => s.code === o.spec)?.options.find((x) => x.code === o.option)?.value ?? o.option).join(" / ");
  const input = "h-10 w-full min-w-20 rounded-xl border border-border bg-surface px-2 text-sm";

  function toggleAxis(code: string, on: boolean) {
    setAxes(on ? [...axes, code] : axes.filter((a) => a !== code));
  }

  function toggleOption(spec: string, option: string, on: boolean) {
    const current = chosen[spec] ?? [];
    setChosen({ ...chosen, [spec]: on ? [...current, option] : current.filter((o) => o !== option) });
  }

  function build() {
    const plan = ordered.map((s) => ({ spec: s.code, options: s.options.map((o) => o.code).filter((o) => (chosen[s.code] ?? []).includes(o)) }));
    setRows(buildVariants(plan, rows, productSku));
  }

  const setRow = (i: number, patch: Partial<VariantRow>) => setRows(rows.map((r, j) => (j === i ? { ...r, ...patch } : r)));

  async function save() {
    setSaved(false);
    const result = await submit(`/api/v1/admin/products/${number}/variants`, toVariantsRequest(rows.length > 0 ? ordered.map((s) => s.code) : [], rows), "PUT");
    if (!result.ok) return;
    setSaved(true);
    await refreshStorefront(locale, ["catalog"]);
    router.refresh();
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted">{texts.variantsIntro}</p>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.saved}</FormAlert>}

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-bold">{texts.variantSpecs}</legend>
        {optionSpecs.length === 0 && <p className="text-sm text-muted">{texts.variantNoOptionSpecs}</p>}
        <div className="flex flex-wrap gap-3">
          {optionSpecs.map((spec) => {
            const blocked = productSpecCodes.includes(spec.code);
            return (
              <label key={spec.code} className={`flex min-h-9 items-center gap-2 text-sm ${blocked ? "text-muted" : ""}`} title={blocked ? texts.variantSpecBlocked : undefined}>
                <input
                  type="checkbox"
                  checked={axes.includes(spec.code)}
                  disabled={blocked || (!axes.includes(spec.code) && axes.length >= maxVariantSpecs)}
                  onChange={(e) => toggleAxis(spec.code, e.target.checked)}
                  className="h-4 w-4"
                />
                {spec.name}
              </label>
            );
          })}
        </div>
      </fieldset>

      {ordered.map((spec) => (
        <fieldset key={spec.code} className="flex flex-col gap-2 rounded-xl border border-border p-3">
          <legend className="px-1 text-sm font-bold">{spec.name}</legend>
          <div className="flex flex-wrap gap-3">
            {spec.options.map((option) => (
              <label key={option.code} className="flex min-h-9 items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={(chosen[spec.code] ?? []).includes(option.code)}
                  onChange={(e) => toggleOption(spec.code, option.code, e.target.checked)}
                  className="h-4 w-4"
                />
                {option.value}
              </label>
            ))}
          </div>
        </fieldset>
      ))}

      {ordered.length > 0 && (
        <div>
          <Button variant="secondary" onClick={build}>
            {texts.variantsBuild}
          </Button>
        </div>
      )}

      {rows.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[40rem] text-sm">
            <thead className="text-start text-xs text-muted">
              <tr>
                <th className="p-1 text-start font-medium">{texts.variantName}</th>
                <th className="p-1 text-start font-medium">{texts.productSku}</th>
                <th className="p-1 text-start font-medium">{texts.variantPrice}</th>
                <th className="p-1 text-start font-medium">{texts.productCompareAt}</th>
                <th className="p-1 text-start font-medium">{texts.productStock}</th>
                <th className="p-1" />
              </tr>
            </thead>
            <tbody>
              {rows.map((row, i) => (
                <tr key={key(row.options)} className="border-t border-border">
                  <td className="p-1 font-medium">{label(row.options)}</td>
                  <td className="p-1">
                    <input aria-label={`${texts.productSku} ${label(row.options)}`} value={row.sku} onChange={(e) => setRow(i, { sku: e.target.value })} maxLength={64} dir="ltr" className={input} />
                  </td>
                  <td className="p-1">
                    <input aria-label={`${texts.variantPrice} ${label(row.options)}`} value={row.price} onChange={(e) => setRow(i, { price: e.target.value })} placeholder={texts.variantPriceHint} inputMode="decimal" dir="ltr" className={input} />
                  </td>
                  <td className="p-1">
                    <input aria-label={`${texts.productCompareAt} ${label(row.options)}`} value={row.compareAtPrice} onChange={(e) => setRow(i, { compareAtPrice: e.target.value })} inputMode="decimal" dir="ltr" className={input} />
                  </td>
                  <td className="p-1">
                    <input aria-label={`${texts.productStock} ${label(row.options)}`} value={row.stock} onChange={(e) => setRow(i, { stock: e.target.value })} inputMode="numeric" dir="ltr" className={input} />
                  </td>
                  <td className="p-1">
                    <Button variant="ghost" size="sm" onClick={() => setRows(rows.filter((_, j) => j !== i))}>
                      {texts.remove}
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div>
        <Button onClick={save} disabled={busy}>
          {texts.variantsSave}
        </Button>
      </div>
    </div>
  );
}
