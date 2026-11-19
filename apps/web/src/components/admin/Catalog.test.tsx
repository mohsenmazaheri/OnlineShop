// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import type { components } from "@/lib/api/schema";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn(), replace: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));
vi.mock("next/link", () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));

import { CatalogTreeEditor, displayName, nodeEndpoints } from "./CatalogTreeEditor";
import { LocalizedFields, readLocalizedFields } from "./LocalizedFields";
import { ProductEditor, groupOptions, parseNumber } from "./ProductEditor";
import { ProductList } from "./ProductList";

type CatalogTree = components["schemas"]["CatalogTree"];
const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const apiCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).startsWith("/api/v1/"));

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  router.replace.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const tree: CatalogTree = {
  defaultLocale: "fa",
  locales: ["fa", "en"],
  categories: [
    {
      id: "c1",
      imageUrl: null,
      texts: [{ locale: "fa", name: "کتاب", slug: "books", description: null }, { locale: "en", name: "Books", slug: "books", description: null }],
      subCategories: [
        {
          id: "s1",
          texts: [{ locale: "fa", name: "رمان", slug: "novels", description: null }, { locale: "en", name: "", slug: null, description: null }],
          groups: [{ id: "g1", texts: [{ locale: "fa", name: "رمان ایرانی", slug: null, description: null }, { locale: "en", name: "", slug: null, description: null }], productCount: 3 }],
          templateSpecs: [],
        },
      ],
      templateSpecs: ["weight"],
    },
  ],
};
const names = { fa: "فارسی", en: "English" };

describe("catalog helpers", () => {
  it("builds the endpoints of each kind of node", () => {
    expect(nodeEndpoints("category", "c1")).toEqual({ create: "/api/v1/admin/catalog/categories", item: "/api/v1/admin/catalog/categories/c1" });
    expect(nodeEndpoints("subcategory", undefined, "c1").create).toBe("/api/v1/admin/catalog/categories/c1/subcategories");
    expect(nodeEndpoints("group", "g1", "s1")).toEqual({ create: "/api/v1/admin/catalog/subcategories/s1/groups", item: "/api/v1/admin/catalog/groups/g1" });
  });

  it("names a node in the admin's language when translated, else in the default", () => {
    expect(displayName(tree.categories[0].texts, "en")).toBe("Books");
    expect(displayName(tree.categories[0].subCategories[0].texts, "en")).toBe("رمان");
    expect(groupOptions(tree, "en")).toEqual([{ value: "g1", label: "Books › رمان › رمان ایرانی" }]);
  });

  it("reads numbers typed with Persian digits, separators or a decimal comma", () => {
    expect(parseNumber("۱۲۳٬۴۵۶")).toBe(123456);
    expect(parseNumber("6,7")).toBe(6.7);
    expect(parseNumber("۶٫۷")).toBe(6.7);
    expect(parseNumber("1,234.5")).toBe(1234.5);
    expect(parseNumber("")).toBeNull();
    expect(parseNumber("abc")).toBeNull();
  });
});

describe("LocalizedFields", () => {
  it("marks untranslated languages and reads every language back", () => {
    const { container } = render(
      <form>
        <LocalizedFields locales={["fa", "en"]} defaultLocale="fa" values={[{ locale: "fa", name: "کتاب", slug: null, description: "x" }]} languageNames={names} texts={texts} withSlug withDescription />
      </form>,
    );

    expect(screen.getByText(texts.defaultLanguage)).toBeTruthy();
    expect(within(screen.getByRole("group", { name: /English/ })).getByText(texts.untranslated)).toBeTruthy();
    expect(readLocalizedFields(new FormData(container.querySelector("form")!), ["fa", "en"])).toEqual([
      { locale: "fa", name: "کتاب", slug: null, description: "x" },
      { locale: "en", name: "", slug: null, description: null },
    ]);
  });
});

describe("CatalogTreeEditor", () => {
  it("shows the tree and adds a category, then refreshes the shop and the page", async () => {
    respond(200, { id: "c2", texts: [] });
    respond(200, { refreshed: ["catalog"] });
    render(<CatalogTreeEditor locale="en" texts={texts} tree={tree} specs={[]} languageNames={names} />);

    expect(screen.getByText("Books")).toBeTruthy();
    expect(screen.getByText(texts.productCount.replace("{count}", "3"))).toBeTruthy();

    const add = screen.getByText(`+ ${texts.addCategory}`).closest("details")!;
    fireEvent.change(within(add).getAllByLabelText(texts.nameLabel)[0], { target: { value: "اسباب‌بازی" } });
    fireEvent.click(within(add).getByRole("button", { name: texts.add }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    const [url, init] = apiCalls()[0];
    expect(url).toBe("/api/v1/admin/catalog/categories");
    expect(JSON.parse(String((init as RequestInit).body)).texts[0]).toEqual({ locale: "fa", name: "اسباب‌بازی", slug: null, description: null });
    expect(fetchMock.mock.calls.some(([u]) => u === "/en/admin/refresh")).toBe(true);
  });

  it("shows the API's reason when a node cannot be deleted", async () => {
    vi.stubGlobal("confirm", () => true);
    respond(409, { status: 409, code: "catalog.has_products" });
    render(<CatalogTreeEditor locale="en" texts={texts} tree={tree} specs={[]} languageNames={names} />);

    const group = screen.getByText("رمان ایرانی").closest("details")!;
    fireEvent.click(within(group).getByRole("button", { name: texts.remove }));

    await waitFor(() => expect(within(group).getByText(texts.catalogErrors.has_products)).toBeTruthy());
    expect((apiCalls()[0][1] as RequestInit).method).toBe("DELETE");
  });
});

describe("ProductEditor", () => {
  const specs = [
    { code: "color", name: "Colour", groupName: "General", kind: "Option", unit: null, options: [{ code: "red", value: "Red" }] },
    { code: "weight", name: "Weight", groupName: "Size", kind: "Number", unit: "g", options: [] },
  ];
  const brands = [{ id: "b1", logoUrl: null, productCount: 0, texts: [{ locale: "fa", name: "اورویا", slug: "orvia", description: null }, { locale: "en", name: "Orvia", slug: "orvia", description: null }] }];

  it("creates a product with texts, numbers and specs, then opens it", async () => {
    respond(200, { number: 1050 });
    render(<ProductEditor locale="en" texts={texts} tree={tree} specs={specs} brands={brands} languageNames={names} />);

    fireEvent.change(screen.getByLabelText(texts.productSku), { target: { value: "N-1" } });
    fireEvent.change(screen.getByLabelText(texts.productPrice), { target: { value: "۱۲۰٬۰۰۰" } });
    fireEvent.change(screen.getByLabelText(texts.productGroup), { target: { value: "g1" } });
    // The category's template offers "weight" at once; left empty, it is not sent.
    expect(screen.getByLabelText("Weight")).toBeTruthy();
    fireEvent.change(screen.getByLabelText(texts.productBrand), { target: { value: "b1" } });
    fireEvent.change(within(screen.getByRole("group", { name: /فارسی/ })).getByLabelText(texts.nameLabel), { target: { value: "کالا" } });
    fireEvent.change(screen.getByLabelText(texts.productAddSpec), { target: { value: "color" } });
    fireEvent.change(screen.getByLabelText("Colour"), { target: { value: "red" } });
    fireEvent.click(screen.getByRole("button", { name: texts.productCreate }));

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/en/admin/products/1050"));
    const body = JSON.parse(String((apiCalls()[0][1] as RequestInit).body));
    expect(body).toMatchObject({ sku: "N-1", price: 120000, compareAtPrice: null, productGroupId: "g1", isPublished: false, brandId: "b1" });
    expect(body.specs).toEqual([{ code: "color", number: null, boolean: null, text: null, optionCode: "red" }]);
  });

  it("saves an existing product with PUT and shows the error of a taken SKU", async () => {
    respond(409, { status: 409, code: "catalog.sku_taken" });
    const product = {
      number: 1001, sku: "A", price: 10, compareAtPrice: null, stock: 1, isPublished: true, productGroupId: "g1",
      texts: [{ locale: "fa", name: "کالا", slug: "kala", description: null }], specs: [], images: [], brandId: null,
      variants: { specCodes: [], variants: [] },
    };
    render(<ProductEditor locale="en" texts={texts} tree={tree} specs={specs} brands={brands} languageNames={names} product={product} />);

    fireEvent.click(screen.getByRole("button", { name: texts.save }));

    await waitFor(() => expect(screen.getByText(texts.catalogErrors.sku_taken)).toBeTruthy());
    expect(apiCalls()[0][0]).toBe("/api/v1/admin/products/1001");
    expect((apiCalls()[0][1] as RequestInit).method).toBe("PUT");
    expect(screen.getByRole("link", { name: texts.productView }).getAttribute("href")).toBe("/en/p/1001");
  });
});

describe("ProductList", () => {
  it("lists products with their state and links to pages", () => {
    render(
      <ProductList
        locale="en"
        texts={texts}
        search="x"
        currency="IRR"
        page={{
          items: [{ number: 1001, name: "Phone", sku: "P1", price: 1000, stock: 0, isPublished: false, groupPath: "A › B › C", imageUrl: null, missingLocales: ["de"] }],
          total: 45,
          page: 2,
          pageSize: 20,
        }}
      />,
    );

    expect(screen.getByRole("link", { name: /Phone/ }).getAttribute("href")).toBe("/en/admin/products/1001");
    expect(screen.getByText(texts.productDraft)).toBeTruthy();
    expect(screen.getByText(texts.productOutOfStock)).toBeTruthy();
    expect(screen.getByText(`${texts.untranslated}: de`)).toBeTruthy();
    const pages = screen.getByRole("navigation", { name: texts.pages });
    expect(within(pages).getAllByRole("link")).toHaveLength(3);
    expect(within(pages).getByRole("link", { name: "2" }).getAttribute("aria-current")).toBe("page");
    expect(within(pages).getByRole("link", { name: "3" }).getAttribute("href")).toBe("/en/admin/products?search=x&page=3");
  });
});
