import type { IconName } from "@/components/ui/Icon";

export type AdminSection = "home" | "products" | "import" | "catalog" | "brands" | "specs" | "seo" | "staff" | "currencies" | "branding" | "inventory" | "movements" | "purchases" | "stocktakes" | "suppliers" | "warehouses";
export type AdminGroup = "catalog" | "inventory" | "store" | "team";

type SectionDefinition = {
  section: Exclude<AdminSection, "home">;
  /** Path below /{locale}/admin. */
  path: string;
  label: `nav${string}`;
  icon: IconName;
  group: AdminGroup;
  /** The permission needed to open it (the API checks it again). */
  permission: string;
};

/** Every admin section in menu order. Adding one here adds it to the panel menu, the start page and the shop header. */
export const adminSections: readonly SectionDefinition[] = [
  { section: "products", path: "/products", label: "navProducts", icon: "box", group: "catalog", permission: "catalog.manage" },
  { section: "catalog", path: "/catalog", label: "navCatalog", icon: "grid", group: "catalog", permission: "catalog.manage" },
  { section: "brands", path: "/brands", label: "navBrands", icon: "tag", group: "catalog", permission: "catalog.manage" },
  { section: "specs", path: "/specs", label: "navSpecs", icon: "list", group: "catalog", permission: "catalog.manage" },
  { section: "import", path: "/import", label: "navImport", icon: "upload", group: "catalog", permission: "catalog.manage" },
  { section: "seo", path: "/seo", label: "navSeo", icon: "search", group: "catalog", permission: "catalog.manage" },
  { section: "inventory", path: "/inventory", label: "navInventory", icon: "stock", group: "inventory", permission: "inventory.view" },
  { section: "movements", path: "/inventory/movements", label: "navMovements", icon: "history", group: "inventory", permission: "inventory.view" },
  { section: "purchases", path: "/purchases", label: "navPurchases", icon: "receipt", group: "inventory", permission: "inventory.view" },
  { section: "stocktakes", path: "/stocktakes", label: "navStocktakes", icon: "clipboard", group: "inventory", permission: "inventory.view" },
  { section: "suppliers", path: "/suppliers", label: "navSuppliers", icon: "truck", group: "inventory", permission: "inventory.view" },
  { section: "warehouses", path: "/warehouses", label: "navWarehouses", icon: "warehouse", group: "inventory", permission: "inventory.view" },
  { section: "branding", path: "/branding", label: "navBranding", icon: "palette", group: "store", permission: "settings.manage" },
  { section: "currencies", path: "/currencies", label: "navCurrencies", icon: "coin", group: "store", permission: "settings.manage" },
  { section: "staff", path: "/staff", label: "navStaff", icon: "users", group: "team", permission: "staff.manage" },
];

export const adminGroups: readonly AdminGroup[] = ["catalog", "inventory", "store", "team"];

/** The sections this user may open. */
export function visibleSections(permissions: readonly string[]): SectionDefinition[] {
  return adminSections.filter((s) => permissions.includes(s.permission));
}

/** The visible sections by group, empty groups left out, in menu order. */
export function sectionsByGroup(permissions: readonly string[]): { group: AdminGroup; sections: SectionDefinition[] }[] {
  const visible = visibleSections(permissions);
  return adminGroups.map((group) => ({ group, sections: visible.filter((s) => s.group === group) })).filter((g) => g.sections.length > 0);
}

export function adminHref(locale: string, path = ""): string {
  return `/${locale}/admin${path}`;
}

/** Whether a user can open the admin panel at all (any staff permission). */
export function isStaff(permissions: readonly string[] | undefined): boolean {
  return (permissions?.length ?? 0) > 0;
}
