import type { components } from "@/lib/api/schema";

export type BrowseResult = components["schemas"]["BrowseResult"];
export type BrowseFacets = components["schemas"]["BrowseFacets"];
export type BrowseSortName = "relevance" | "newest" | "price_asc" | "price_desc" | "discount";

/** The address bar's parameters as Next.js gives them. */
export type SearchParams = Record<string, string | string[] | undefined>;

/** The listing's own parameters, one string each (a key given twice, as a form sends checkboxes, becomes a comma list). */
export type ListingParams = Record<string, string>;

const filterKeys = ["brand", "group", "min", "max", "stock", "sale"] as const;
const filterPrefixes = ["o.", "n.", "y.", "nmin.", "nmax."] as const;
const sortNames: readonly BrowseSortName[] = ["relevance", "newest", "price_asc", "price_desc", "discount"];

const isFilterKey = (key: string) => (filterKeys as readonly string[]).includes(key) || filterPrefixes.some((p) => key.startsWith(p) && key.length > p.length);

/** Only the listing's keys (filters, sort, page), trimmed, empty ones dropped; utm_* and anything else are left out. */
export function listingParams(params: SearchParams): ListingParams {
  const result: ListingParams = {};
  for (const [key, raw] of Object.entries(params)) {
    if (!isFilterKey(key) && key !== "sort" && key !== "page") continue;
    const value = (Array.isArray(raw) ? raw : [raw])
      .flatMap((v) => (v ?? "").split(","))
      .map((v) => v.trim())
      .filter(Boolean)
      .join(",");
    if (value) result[key] = value.slice(0, 500);
  }
  if (result.sort && !(sortNames as readonly string[]).includes(result.sort)) delete result.sort;
  if (result.page && !/^[1-9]\d{0,4}$/.test(result.page)) delete result.page;
  return result;
}

/** Whether any filter is set (sort and page are not filters). */
export function hasFilters(params: ListingParams): boolean {
  return Object.keys(params).some(isFilterKey);
}

/** A filtered or re-sorted listing is not indexed (it points at the plain one); a plain page 2 is. */
export function isIndexable(params: ListingParams): boolean {
  return !hasFilters(params) && !params.sort;
}

/** The canonical address of a listing: the plain list, plus its page number from page 2 on. */
export function canonicalPath(basePath: string, params: ListingParams): string {
  const page = Number(params.page ?? "1");
  return page > 1 && isIndexable(params) ? `${basePath}?page=${page}` : basePath;
}

/** The listing's address with some parameters changed (null removes one); any change but the page goes back to page 1. */
export function listingHref(basePath: string, params: ListingParams, changes: Record<string, string | null>): string {
  const next: ListingParams = { ...params };
  for (const [key, value] of Object.entries(changes)) {
    if (value === null || value === "") delete next[key];
    else next[key] = value;
  }
  if (!("page" in changes)) delete next.page;
  if (next.page === "1") delete next.page;
  const query = new URLSearchParams(Object.entries(next).sort(([a], [b]) => a.localeCompare(b))).toString();
  return query ? `${basePath}?${query}` : basePath;
}

/** The comma list with the value added, or removed when it was there (null when nothing is left). */
export function toggled(list: string | undefined, value: string): string | null {
  const values = (list ?? "").split(",").filter(Boolean);
  const next = values.includes(value) ? values.filter((v) => v !== value) : [...values, value];
  return next.length > 0 ? next.join(",") : null;
}

export function listHas(list: string | undefined, value: string): boolean {
  return (list ?? "").split(",").includes(value);
}

/**
 * The API query for a listing. Prices typed in the display currency are turned back into the base currency (with a
 * small margin so a rounded price still matches), and "nmin.x"/"nmax.x" (two inputs of one form) become "n.x=min-max".
 */
export function toApiQuery(scope: { c?: string; s?: string; b?: string; q?: string }, params: ListingParams, locale: string, rate: number): string {
  const query = new URLSearchParams({ locale });
  for (const [key, value] of Object.entries(scope)) if (value) query.set(key, value);
  const ranges: Record<string, { min?: string; max?: string }> = {};
  for (const [key, value] of Object.entries(params)) {
    if (key === "min" || key === "max") {
      const amount = Number(value);
      if (!Number.isFinite(amount) || amount < 0) continue;
      const base = rate > 0 ? amount / rate : amount;
      query.set(key, String(key === "min" ? Math.floor(base * 100) / 100 : Math.ceil(base * 100) / 100));
    } else if (key.startsWith("nmin.") || key.startsWith("nmax.")) {
      const code = key.slice(5);
      ranges[code] = { ...ranges[code], [key.startsWith("nmin.") ? "min" : "max"]: value };
    } else {
      query.set(key, value);
    }
  }
  for (const [code, { min, max }] of Object.entries(ranges)) {
    const clean = (v?: string) => (v && Number.isFinite(Number(v)) ? v : "");
    if (clean(min) || clean(max)) query.set(`n.${code}`, `${clean(min)}-${clean(max)}`);
  }
  return query.toString();
}

/** One applied filter as a removable chip: its text and the address without it. */
export type AppliedFilter = { label: string; href: string };

type ChipTexts = { inStock: string; onSale: string; priceFrom: string; priceTo: string; yes: string };

/** The filters in effect, each with the address that removes it; labels come from the facets (in the reader's language). */
export function appliedFilters(basePath: string, params: ListingParams, facets: BrowseFacets, texts: ChipTexts, money: (amount: number) => string): AppliedFilter[] {
  const chips: AppliedFilter[] = [];
  const without = (key: string, value: string | null) => listingHref(basePath, params, { [key]: value });
  const listChips = (key: string, values: { value: string; label: string }[]) => {
    for (const v of values) if (listHas(params[key], v.value)) chips.push({ label: v.label, href: without(key, toggled(params[key], v.value)) });
  };
  listChips("brand", facets.brands);
  listChips("group", facets.groups);
  if (params.min) chips.push({ label: texts.priceFrom.replace("{price}", money(Number(params.min))), href: without("min", null) });
  if (params.max) chips.push({ label: texts.priceTo.replace("{price}", money(Number(params.max))), href: without("max", null) });
  if (params.stock === "1") chips.push({ label: texts.inStock, href: without("stock", null) });
  if (params.sale === "1") chips.push({ label: texts.onSale, href: without("sale", null) });
  for (const spec of facets.specs) {
    if (spec.kind === "Option") listChips(`o.${spec.code}`, spec.options);
    if (spec.kind === "Boolean" && params[`y.${spec.code}`] === "1") chips.push({ label: `${spec.name}: ${texts.yes}`, href: without(`y.${spec.code}`, null) });
    if (spec.kind === "Number") {
      const [min, max] = [params[`nmin.${spec.code}`], params[`nmax.${spec.code}`]];
      if (min || max) {
        const unit = spec.unit ? ` ${spec.unit}` : "";
        chips.push({
          label: `${spec.name}: ${min ?? ""}–${max ?? ""}${unit}`,
          href: listingHref(basePath, params, { [`nmin.${spec.code}`]: null, [`nmax.${spec.code}`]: null }),
        });
      }
    }
  }
  return chips;
}

/** The address without any filter (the sort and a search's words kept). */
export function clearFiltersHref(basePath: string, params: ListingParams): string {
  const kept = Object.fromEntries(Object.entries(params).filter(([key]) => key === "sort" || key === "q"));
  return listingHref(basePath, kept, {});
}

/** The sorts a listing offers: relevance only for a text search. */
export function sortsFor(isSearch: boolean): BrowseSortName[] {
  return isSearch ? [...sortNames] : sortNames.filter((s) => s !== "relevance");
}

/** The API's sort as the address bar names it. */
export function sortName(sort: BrowseResult["sort"]): BrowseSortName {
  return ({ Relevance: "relevance", Newest: "newest", PriceAsc: "price_asc", PriceDesc: "price_desc", Discount: "discount" } as const)[sort];
}

type RelaxTexts = { brands: string; groups: string; price: string; inStock: string; onSale: string; relaxFilter: string };

/**
 * On an empty result: a link per filter that alone hides products, the one hiding most first ("Without “Brand” (12
 * products)"). The API names the filters as the address bar does; ranges drop both of their inputs.
 */
export function relaxationLinks(basePath: string, params: ListingParams, result: BrowseResult, texts: RelaxTexts, count: (n: number) => string): AppliedFilter[] {
  return result.relaxations.map((relaxation) => {
    const key = relaxation.filter;
    const spec = result.facets.specs.find((s) => key.length > 2 && key.slice(2) === s.code);
    const name =
      ({ brand: texts.brands, group: texts.groups, price: texts.price, stock: texts.inStock, sale: texts.onSale } as Record<string, string>)[key] ?? spec?.name ?? key;
    const changes: Record<string, null> =
      key === "price" ? { min: null, max: null } : key.startsWith("n.") ? { [`nmin.${key.slice(2)}`]: null, [`nmax.${key.slice(2)}`]: null } : { [key]: null };
    return { label: texts.relaxFilter.replace("{filter}", name).replace("{count}", count(relaxation.count)), href: listingHref(basePath, params, changes) };
  });
}
