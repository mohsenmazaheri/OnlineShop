// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import type { DisplayCurrency } from "@/lib/currency/display";
import en from "@/lib/i18n/dictionaries/en.json";
import { VariantPicker } from "./VariantPicker";

afterEach(cleanup);

const eur: DisplayCurrency = { code: "EUR", symbol: null, decimals: 2, roundingStep: 0.01, rate: 1, isBase: true, base: "EUR" };
const axes = [
  { code: "color", name: "Colour", options: [{ code: "beige", value: "Beige" }, { code: "gray", value: "Grey" }] },
  { code: "size", name: "Size", options: [{ code: "m", value: "M" }, { code: "l", value: "L" }] },
];
const offer = (sku: string, color: string, size: string, price: number, inStock = true) => ({
  sku,
  gtin: null,
  price,
  compareAtPrice: null,
  inStock,
  options: [
    { axis: "color", option: color },
    { axis: "size", option: size },
  ],
});
const variants = [offer("BM", "beige", "m", 39, false), offer("BL", "beige", "l", 39), offer("GL", "gray", "l", 45)];

describe("VariantPicker", () => {
  it("opens on the first variant in stock and switches price, stock and SKU with the chips", () => {
    render(<VariantPicker axes={axes} variants={variants} money={eur} locale="en" texts={en.product} />);

    expect(screen.getByRole("button", { name: "L" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByText("BL")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Grey" }));
    expect(screen.getByText("GL")).toBeTruthy();
    expect(screen.getByText(/45/)).toBeTruthy();

    // Beige M is sold out: still choosable, struck through, and the box says so.
    fireEvent.click(screen.getByRole("button", { name: "Beige" }));
    const m = screen.getByRole("button", { name: "M" });
    expect(m.className).toContain("line-through");
    fireEvent.click(m);
    expect(screen.getByText(en.product.outOfStock)).toBeTruthy();
  });
});
