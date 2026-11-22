"use client";

import { useState } from "react";
import type { DisplayCurrency } from "@/lib/currency/display";
import { defaultVariant, optionState, selectVariant, type VariantAxis, type VariantOffer } from "@/lib/catalog/variants";
import { AddToCartButton, type AddToCartTexts } from "@/components/cart/AddToCartButton";
import type { ErrorTexts } from "@/lib/account/problems";
import { Price, type PriceTexts } from "./Price";

type Texts = PriceTexts & AddToCartTexts & { inStock: string; outOfStock: string; sku: string; soldOutOption: string };

type Props = {
  axes: VariantAxis[];
  variants: VariantOffer[];
  money: DisplayCurrency;
  locale: string;
  texts: Texts;
  /** The product's public number, for the add-to-cart button of the chosen variant. */
  number: number;
  cartErrors: ErrorTexts;
};

/**
 * Colour/size choice in the price box. The server renders the default variant (first in stock), so the price, stock
 * and SKU are right without JavaScript; with it, the chips switch the variant. Sold-out options stay choosable (struck
 * through) so the visitor sees why.
 */
export function VariantPicker({ axes, variants, money, locale, texts, number, cartErrors }: Props) {
  const [current, setCurrent] = useState(() => defaultVariant(variants));
  if (!current) return null;

  return (
    <div className="flex flex-col gap-4">
      {axes.map((axis) => {
        const chosen = axis.options.find((o) => current.options.some((c) => c.axis === axis.code && c.option === o.code));
        return (
          <fieldset key={axis.code} className="flex flex-col gap-2">
            <legend className="text-sm">
              <span className="text-muted">{axis.name}: </span>
              <span className="font-bold">{chosen?.value}</span>
            </legend>
            <div className="flex flex-wrap gap-2">
              {axis.options.map((option) => {
                const state = optionState(variants, current, axis.code, option.code);
                if (state === "unavailable") return null;
                return (
                  <button
                    key={option.code}
                    type="button"
                    aria-pressed={state === "selected"}
                    title={state === "soldOut" ? texts.soldOutOption : undefined}
                    onClick={() => setCurrent(selectVariant(variants, current, axis.code, option.code))}
                    className={`min-h-10 min-w-11 rounded-xl border px-3 text-sm font-medium ${
                      state === "selected" ? "border-primary bg-primary/10 text-primary" : "border-border hover:border-primary"
                    } ${state === "soldOut" ? "text-muted line-through" : ""}`}
                  >
                    {option.value}
                  </button>
                );
              })}
            </div>
          </fieldset>
        );
      })}

      <div aria-live="polite" className="flex flex-col gap-3">
        <Price price={current.price} compareAtPrice={current.compareAtPrice} money={money} locale={locale} texts={texts} size="lg" />
        <p className={`flex items-center gap-2 text-sm font-medium ${current.inStock ? "text-success" : "text-danger"}`}>
          <span aria-hidden className="h-2 w-2 rounded-full bg-current" />
          {current.inStock ? texts.inStock : texts.outOfStock}
        </p>
        <p className="text-xs text-muted">
          {texts.sku}: <span dir="ltr">{current.sku}</span>
        </p>
      </div>
      {/* Keyed by the variant, so a confirmation does not linger after switching. */}
      <AddToCartButton key={current.sku} locale={locale} number={number} variantSku={current.sku} inStock={current.inStock} texts={texts} errors={cartErrors} />
    </div>
  );
}
