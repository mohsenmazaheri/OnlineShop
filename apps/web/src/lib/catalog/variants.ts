import type { components } from "@/lib/api/schema";

export type VariantAxis = components["schemas"]["VariantAxis"];
export type VariantOffer = components["schemas"]["VariantOffer"];

/** The option a variant has on one axis (its code), or undefined. */
export function optionOf(variant: VariantOffer, axis: string): string | undefined {
  return variant.options.find((o) => o.axis === axis)?.option;
}

/** The variant the page opens with: the first in stock, otherwise the first. */
export function defaultVariant(variants: readonly VariantOffer[]): VariantOffer | undefined {
  return variants.find((v) => v.inStock) ?? variants[0];
}

/**
 * The variant after choosing an option on one axis: the one that keeps every other current choice, otherwise the
 * closest one with that option (in stock first, then the most matching other choices).
 */
export function selectVariant(variants: readonly VariantOffer[], current: VariantOffer | undefined, axis: string, option: string): VariantOffer | undefined {
  const candidates = variants.filter((v) => optionOf(v, axis) === option);
  if (candidates.length === 0) return current;
  const matches = (v: VariantOffer) => (current ? current.options.filter((o) => o.axis !== axis && optionOf(v, o.axis) === o.option).length : 0);
  return [...candidates].sort((a, b) => matches(b) - matches(a) || Number(b.inStock) - Number(a.inStock))[0];
}

/** How an option chip looks next to the current variant: chosen, buyable, sold out, or not offered with the other choices. */
export function optionState(
  variants: readonly VariantOffer[],
  current: VariantOffer | undefined,
  axis: string,
  option: string,
): "selected" | "available" | "soldOut" | "unavailable" {
  if (current && optionOf(current, axis) === option) return "selected";
  const withOthers = variants.filter(
    (v) => optionOf(v, axis) === option && (current?.options ?? []).every((o) => o.axis === axis || optionOf(v, o.axis) === o.option),
  );
  if (withOthers.some((v) => v.inStock)) return "available";
  if (withOthers.length > 0) return "soldOut";
  return variants.some((v) => optionOf(v, axis) === option) ? "available" : "unavailable";
}

/** "Beige / M": the variant's option values in axis order. */
export function variantLabel(variant: VariantOffer, axes: readonly VariantAxis[]): string {
  return axes
    .map((a) => a.options.find((o) => o.code === optionOf(variant, a.code))?.value)
    .filter(Boolean)
    .join(" / ");
}
