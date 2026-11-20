import { describe, expect, it } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import {
  allKinds,
  manualKinds,
  movementsHref,
  pageWindow,
  parseId,
  parseKind,
  parseStocktakeFilter,
  purchaseLabel,
  referenceLabel,
  signedQuantity,
  stocktakeFilters,
  stocktakeHref,
  stocktakeLabel,
} from "./inventory";

describe("inventory helpers", () => {
  it("translates automatic references and keeps typed ones", () => {
    expect(referenceLabel("auto:editor", en.admin.stockReferences)).toBe("Product editor");
    expect(referenceLabel("auto:unknown", en.admin.stockReferences)).toBe("auto:unknown");
    expect(referenceLabel("PO-12", en.admin.stockReferences)).toBe("PO-12");
    expect(referenceLabel(null, en.admin.stockReferences)).toBeNull();
  });

  it("shows quantities with their sign in the locale's digits", () => {
    expect(signedQuantity(5, "en")).toBe("+5");
    expect(signedQuantity(-1200, "en")).toBe("−1,200");
    expect(signedQuantity(3, "fa")).toBe("+۳");
    expect(signedQuantity(0, "en")).toBe("0");
  });

  it("reads filters from the address bar safely and builds ledger addresses", () => {
    expect(parseKind("Receipt")).toBe("Receipt");
    expect(parseKind("drop table")).toBeUndefined();
    expect(parseId("0195f6a1-2b3c-7d4e-8f90-a1b2c3d4e5f6")).toBe("0195f6a1-2b3c-7d4e-8f90-a1b2c3d4e5f6");
    expect(parseId("x")).toBeUndefined();
    expect(movementsHref("fa", {})).toBe("/fa/admin/inventory/movements");
    expect(movementsHref("en", { sku: "A 1", kind: "Issue", page: 2 })).toBe("/en/admin/inventory/movements?sku=A+1&kind=Issue&page=2");
    expect(movementsHref("en", { page: 1 })).toBe("/en/admin/inventory/movements");
  });

  it("shows a window of page links around the current page", () => {
    expect(pageWindow(1, 1)).toEqual([1]);
    expect(pageWindow(1, 3)).toEqual([1, 2, 3]);
    expect(pageWindow(10, 40)).toEqual([1, 8, 9, 10, 11, 12, 40]);
    expect(pageWindow(40, 40)).toEqual([1, 38, 39, 40]);
  });

  it("reads stocktake filters and builds stocktake addresses", () => {
    expect(parseStocktakeFilter("Differences")).toBe("Differences");
    expect(parseStocktakeFilter("x")).toBe("All");
    expect(stocktakeLabel(7)).toBe("ST-7");
    expect(purchaseLabel(3)).toBe("PR-3");
    expect(stocktakeHref("fa", 7)).toBe("/fa/admin/stocktakes/7");
    expect(stocktakeHref("en", 7, { filter: "All", search: "a b", page: 3 })).toBe("/en/admin/stocktakes/7?search=a+b&page=3");
    expect(stocktakeHref("en", 7, { filter: "Uncounted", page: 1 })).toBe("/en/admin/stocktakes/7?filter=Uncounted");
    for (const filter of stocktakeFilters) expect(en.admin.stocktakeFilters[filter]).toBeTruthy();
  });

  it("has a text for every kind and every automatic reference", () => {
    for (const kind of allKinds) expect(en.admin.movementKinds[kind]).toBeTruthy();
    expect(manualKinds.every((k) => allKinds.includes(k))).toBe(true);
    expect(Object.keys(en.admin.stockReferences).sort()).toEqual(["editor", "import", "opening", "variants"]);
  });
});
