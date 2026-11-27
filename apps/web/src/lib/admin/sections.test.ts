import { describe, expect, it } from "vitest";
import { iconNames } from "@/components/ui/Icon";
import en from "@/lib/i18n/dictionaries/en.json";
import { adminHref, adminSections, isStaff, sectionsByGroup, visibleSections } from "./sections";

describe("admin sections", () => {
  it("shows each user only the sections of their permissions, grouped in menu order", () => {
    expect(visibleSections(["staff.manage"]).map((s) => s.section)).toEqual(["staff"]);
    // Shipping and tax (settings.manage) sit with the orders under Sales.
    expect(sectionsByGroup(["settings.manage", "catalog.manage"]).map((g) => g.group)).toEqual(["sales", "catalog", "store"]);
    expect(sectionsByGroup([])).toEqual([]);
    // The warehouse role sees the orders (to pack them) and the inventory.
    expect(sectionsByGroup(["inventory.view", "inventory.manage", "orders.view"]).map((g) => g.group)).toEqual(["sales", "inventory"]);
    expect(visibleSections(["orders.view"]).map((s) => s.section)).toEqual(["orders"]);
    expect(visibleSections(["inventory.view"]).map((s) => s.section)).toEqual(["inventory", "movements", "purchases", "reservations", "stocktakes", "suppliers", "warehouses"]);
    expect(visibleSections(["catalog.manage"]).map((s) => s.section)).toEqual(["products", "catalog", "brands", "specs", "import", "seo"]);
  });

  it("knows staff by any permission and builds the addresses", () => {
    expect(isStaff(["orders.view"])).toBe(true);
    expect(isStaff([])).toBe(false);
    expect(isStaff(undefined)).toBe(false);
    expect(adminHref("fa", "/products")).toBe("/fa/admin/products");
    expect(adminHref("fa")).toBe("/fa/admin");
  });

  it("every section has a label, a description, a group title and an icon", () => {
    for (const s of adminSections) {
      expect(en.admin[s.label as keyof typeof en.admin]).toBeTruthy();
      expect(en.admin.sectionHints[s.section]).toBeTruthy();
      expect(en.admin.groups[s.group]).toBeTruthy();
      expect(iconNames).toContain(s.icon);
    }
    expect(new Set(adminSections.map((s) => s.path)).size).toBe(adminSections.length);
  });
});
