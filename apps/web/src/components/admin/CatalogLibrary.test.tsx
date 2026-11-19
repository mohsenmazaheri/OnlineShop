// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn(), replace: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));
vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));

import { BrandsEditor } from "./BrandsEditor";
import { ProductVariantsEditor, buildVariants, combinations, toVariantsRequest, variantSku, type VariantRow } from "./ProductVariantsEditor";
import { SpecsEditor, specName, toDraft, toRequest } from "./SpecsEditor";
import { TemplatePicker } from "./TemplatePicker";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const apiCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).startsWith("/api/v1/"));
const bodyOf = (i = 0) => JSON.parse(String((apiCalls()[i][1] as RequestInit).body));
const names = { fa: "فارسی", en: "English" };

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("BrandsEditor", () => {
  const list = {
    defaultLocale: "fa",
    locales: ["fa", "en"],
    brands: [
      {
        id: "b1",
        logoUrl: null,
        productCount: 2,
        texts: [
          { locale: "fa", name: "اورویا", slug: "orvia", description: null },
          { locale: "en", name: "Orvia", slug: "orvia", description: null },
        ],
      },
    ],
  };

  it("lists brands in the admin's language and adds one", async () => {
    respond(200, { id: "b2" });
    respond(200, { refreshed: ["catalog"] });
    render(<BrandsEditor locale="en" texts={texts} list={list} languageNames={names} />);

    expect(screen.getByText("Orvia")).toBeTruthy();
    const add = screen.getByText(`+ ${texts.addBrand}`).closest("details")!;
    fireEvent.change(within(add).getAllByLabelText(texts.nameLabel)[0], { target: { value: "نوا" } });
    fireEvent.click(within(add).getByRole("button", { name: texts.add }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(apiCalls()[0][0]).toBe("/api/v1/admin/brands");
    expect(bodyOf().texts[0]).toEqual({ locale: "fa", name: "نوا", slug: null, description: null });
  });

  it("does not offer deleting a brand that products use", () => {
    render(<BrandsEditor locale="en" texts={texts} list={list} languageNames={names} />);

    const brand = screen.getByText("Orvia").closest("details")!;
    expect((within(brand).getAllByRole("button", { name: texts.remove })[0] as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("SpecsEditor", () => {
  const color = {
    id: "s1",
    code: "color",
    kind: "Option",
    isFilterable: true,
    isComparable: true,
    showInSummary: false,
    usageCount: 3,
    texts: [
      { locale: "fa", name: "رنگ", groupName: "کلی", unit: null },
      { locale: "en", name: "Colour", groupName: "General", unit: null },
    ],
    options: [{ code: "red", usageCount: 1, texts: [{ locale: "fa", value: "قرمز" }, { locale: "en", value: "Red" }] }],
  };
  const list = { defaultLocale: "fa", locales: ["fa", "en"], specs: [color] };

  it("turns a spec into a draft and back, with options only for the option kind", () => {
    const draft = toDraft(color, ["fa", "en", "de"]);
    expect(draft.texts.map((t) => t.locale)).toEqual(["fa", "en", "de"]);
    expect(draft.options[0].texts.find((t) => t.locale === "de")).toEqual({ locale: "de", value: "" });

    expect(toRequest({ ...draft, code: " " }).code).toBeNull();
    expect(toRequest({ ...draft, kind: "Number" }).options).toEqual([]);
    expect(toRequest(draft).texts[2]).toEqual({ locale: "de", name: "", groupName: null, unit: null });
    expect(specName(color.texts, "de")).toBe("رنگ");
  });

  it("keeps the kind of a used spec and adds a new option on save", async () => {
    respond(200, color);
    respond(200, { refreshed: ["catalog"] });
    render(<SpecsEditor locale="en" texts={texts} list={list} languageNames={names} />);

    const spec = screen.getByText("Colour").closest("details")!;
    expect((within(spec).getByLabelText(texts.specKind) as HTMLSelectElement).disabled).toBe(true);
    expect((within(spec).getAllByRole("button", { name: texts.remove })[0] as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(within(spec).getByRole("button", { name: `+ ${texts.specAddOption}` }));
    const inputs = within(spec).getAllByLabelText("فارسی").filter((e) => e.tagName === "INPUT");
    fireEvent.change(inputs[inputs.length - 1], { target: { value: "سبز" } });
    fireEvent.click(within(spec).getByRole("button", { name: texts.save }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(apiCalls()[0][0]).toBe("/api/v1/admin/specs/s1");
    expect(bodyOf().options.map((o: { code: string | null }) => o.code)).toEqual(["red", null]);
    expect(bodyOf().options[1].texts[0]).toEqual({ locale: "fa", value: "سبز" });
  });
});

describe("TemplatePicker", () => {
  const specs = [
    { code: "color", name: "Colour", groupName: null, kind: "Option", unit: null, options: [] },
    { code: "weight", name: "Weight", groupName: "Size", kind: "Number", unit: "g", options: [] },
  ];

  it("saves the ticked specs, leaving out the category's (shown fixed)", async () => {
    respond(200, ["weight"]);
    render(<TemplatePicker texts={texts} path="/api/v1/admin/catalog/subcategories/s1/template" specs={specs} selected={[]} inherited={["color"]} />);

    expect((screen.getByLabelText("Colour") as HTMLInputElement).disabled).toBe(true);
    fireEvent.click(screen.getByLabelText("Size › Weight"));
    fireEvent.click(screen.getByRole("button", { name: texts.save }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(bodyOf()).toEqual({ specCodes: ["weight"] });
  });
});

describe("variant helpers", () => {
  const axes = [
    { spec: "color", options: ["beige", "gray"] },
    { spec: "size", options: ["m", "l"] },
  ];

  it("builds every combination, keeping existing rows", () => {
    expect(combinations(axes)).toHaveLength(4);
    expect(combinations([])).toEqual([]);
    expect(variantSku("tee-1", [{ spec: "color", option: "beige" }, { spec: "size", option: "m" }])).toBe("TEE-1-BEIGE-M");

    const kept: VariantRow = { id: "v1", sku: "OWN", gtin: "", price: "", compareAtPrice: "", stock: "5", options: [{ spec: "size", option: "m" }, { spec: "color", option: "beige" }] };
    const rows = buildVariants(axes, [kept], "TEE");
    expect(rows.map((r) => r.sku)).toEqual(["OWN", "TEE-BEIGE-L", "TEE-GRAY-M", "TEE-GRAY-L"]);
    expect(rows[1]).toMatchObject({ id: null, stock: "0" });
  });

  it("sends numbers parsed, empty prices as the product's", () => {
    const request = toVariantsRequest(["size"], [{ id: null, sku: " A ", gtin: " 96385074 ", price: "", compareAtPrice: "۵۰", stock: "۳", options: [{ spec: "size", option: "m" }] }]);
    expect(request).toEqual({ specCodes: ["size"], variants: [{ id: null, sku: "A", gtin: "96385074", price: null, compareAtPrice: 50, stock: 3, options: [{ spec: "size", option: "m" }] }] });
  });
});

describe("ProductVariantsEditor", () => {
  const specs = [
    { code: "color", name: "Colour", groupName: null, kind: "Option", unit: null, options: [{ code: "beige", value: "Beige" }, { code: "gray", value: "Grey" }] },
    { code: "size", name: "Size", groupName: null, kind: "Option", unit: null, options: [{ code: "m", value: "M" }, { code: "l", value: "L" }] },
    { code: "weight", name: "Weight", groupName: null, kind: "Number", unit: "g", options: [] },
  ];

  it("builds the combinations of the chosen options and saves them as one set", async () => {
    respond(200, { specCodes: ["size"], variants: [] });
    respond(200, { refreshed: ["catalog"] });
    render(<ProductVariantsEditor locale="en" number={1001} productSku="TEE" specs={specs} productSpecCodes={["color"]} view={{ specCodes: [], variants: [] }} texts={texts} />);

    // Only option specs are offered, and not one the product has its own value for.
    expect(screen.queryByLabelText("Weight")).toBeNull();
    expect((screen.getByLabelText("Colour") as HTMLInputElement).disabled).toBe(true);
    fireEvent.click(screen.getByLabelText("Size"));
    fireEvent.click(screen.getByLabelText("M"));
    fireEvent.click(screen.getByLabelText("L"));
    fireEvent.click(screen.getByRole("button", { name: texts.variantsBuild }));
    fireEvent.change(screen.getByLabelText(`${texts.productStock} L`), { target: { value: "4" } });
    fireEvent.click(screen.getByRole("button", { name: texts.variantsSave }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(apiCalls()[0][0]).toBe("/api/v1/admin/products/1001/variants");
    expect(bodyOf().specCodes).toEqual(["size"]);
    expect(bodyOf().variants.map((v: { sku: string; stock: number }) => [v.sku, v.stock])).toEqual([
      ["TEE-M", 0],
      ["TEE-L", 4],
    ]);
  });

  it("shows why a set was refused", async () => {
    respond(400, { status: 400, code: "catalog.variant_repeated" });
    const view = { specCodes: ["size"], variants: [{ id: "v1", sku: "A", gtin: null, price: null, compareAtPrice: null, stock: 1, options: [{ spec: "size", option: "m" }] }] };
    render(<ProductVariantsEditor locale="en" number={1001} productSku="TEE" specs={specs} productSpecCodes={[]} view={view} texts={texts} />);

    expect((screen.getByLabelText("M") as HTMLInputElement).checked).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: texts.variantsSave }));

    await waitFor(() => expect(screen.getByText(texts.catalogErrors.variant_repeated)).toBeTruthy());
    expect(bodyOf().variants[0].id).toBe("v1");
  });
});
