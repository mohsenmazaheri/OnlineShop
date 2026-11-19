import { describe, expect, it } from "vitest";
import { defaultVariant, optionState, selectVariant, variantLabel, type VariantAxis, type VariantOffer } from "./variants";

const v = (sku: string, color: string, size: string, inStock = true): VariantOffer => ({
  sku,
  price: 10,
  compareAtPrice: null,
  inStock,
  options: [
    { axis: "color", option: color },
    { axis: "size", option: size },
  ],
});

const variants = [v("BS", "beige", "s", false), v("BM", "beige", "m"), v("GS", "gray", "s"), v("GM", "gray", "m", false)];
const axes: VariantAxis[] = [
  { code: "color", name: "Colour", options: [{ code: "beige", value: "Beige" }, { code: "gray", value: "Grey" }] },
  { code: "size", name: "Size", options: [{ code: "s", value: "S" }, { code: "m", value: "M" }] },
];

describe("variants", () => {
  it("opens with the first variant in stock", () => {
    expect(defaultVariant(variants)?.sku).toBe("BM");
    expect(defaultVariant([v("X", "a", "b", false)])?.sku).toBe("X");
    expect(defaultVariant([])).toBeUndefined();
  });

  it("keeps the other choices when switching one, else picks the closest in stock", () => {
    const beigeM = variants[1];
    expect(selectVariant(variants, beigeM, "size", "s")?.sku).toBe("BS");
    // Grey keeps size M (sold out) because it matches the other choice; nothing for an unknown option.
    expect(selectVariant(variants, beigeM, "color", "gray")?.sku).toBe("GM");
    expect(selectVariant(variants, beigeM, "color", "red")?.sku).toBe("BM");
    expect(selectVariant(variants, undefined, "color", "gray")?.sku).toBe("GS");
  });

  it("marks chips as selected, available or sold out with the other choices", () => {
    const beigeM = variants[1];
    expect(optionState(variants, beigeM, "color", "beige")).toBe("selected");
    expect(optionState(variants, beigeM, "color", "gray")).toBe("soldOut");
    expect(optionState(variants, beigeM, "size", "s")).toBe("soldOut");
    expect(optionState([v("A", "beige", "s")], v("A", "beige", "s"), "color", "gray")).toBe("unavailable");
  });

  it("labels a variant with its option values in axis order", () => {
    expect(variantLabel(variants[2], axes)).toBe("Grey / S");
  });
});
