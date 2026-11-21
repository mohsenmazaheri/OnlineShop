// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { compareStore } from "@/lib/compare/store";
import type { ProductComparison } from "@/lib/api/storefront";
import en from "@/lib/i18n/dictionaries/en.json";

const { pathname } = vi.hoisted(() => ({ pathname: { value: "/en" } }));
vi.mock("next/navigation", () => ({ usePathname: () => pathname.value }));
vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));

import { CompareBar } from "./CompareBar";
import { CompareButton } from "./CompareButton";
import { CompareTable } from "./CompareTable";

const eur = { code: "EUR", symbol: null, decimals: 2, roundingStep: 0.01, rate: 1, isBase: true, base: "EUR" };

beforeEach(() => {
  pathname.value = "/en";
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 401 })));
});
afterEach(() => {
  act(() => compareStore().clear());
  cleanup();
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

describe("compare button and bar", () => {
  it("adds and removes a product and shows the bar with the count and the comparison link", () => {
    render(
      <>
        <CompareButton locale="en" number={1001} category="phones" texts={en.product} />
        <CompareButton locale="en" number={1002} category="phones" texts={en.product} look="icon" />
        <CompareBar locale="en" texts={en.compare} />
      </>,
    );
    const [full, icon] = screen.getAllByRole("button", { name: en.product.compare });
    expect(full.getAttribute("aria-pressed")).toBe("false");
    expect(screen.queryByRole("complementary")).toBeNull();

    fireEvent.click(full);
    fireEvent.click(icon);
    expect(screen.getByRole("button", { name: en.product.compareAdded }).getAttribute("aria-pressed")).toBe("true");
    const bar = screen.getByRole("complementary", { name: en.compare.barLabel });
    expect(within(bar).getByRole("link", { name: "Compare (2)" }).getAttribute("href")).toBe("/en/compare?ids=1001,1002");

    fireEvent.click(within(bar).getByRole("button", { name: en.compare.clear }));
    expect(screen.queryByRole("complementary")).toBeNull();
  });

  it("asks before starting over with a product of another category", () => {
    render(
      <>
        <CompareButton locale="en" number={1} category="phones" texts={en.product} />
        <CompareButton locale="en" number={2} category="kitchen" texts={en.product} />
        <CompareBar locale="en" texts={en.compare} />
      </>,
    );
    const [phone, pan] = screen.getAllByRole("button", { name: en.product.compare });
    fireEvent.click(phone);
    fireEvent.click(pan);
    expect(screen.getByRole("alert").textContent).toContain(en.compare.otherCategory);

    fireEvent.click(screen.getByRole("button", { name: en.compare.startNew }));
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByRole("link", { name: "Compare (1)" }).getAttribute("href")).toBe("/en/compare?ids=2");
  });

  it("stays out of the comparison page and the admin panel", () => {
    act(() => compareStore().toggle({ n: 1, c: "phones" }));
    pathname.value = "/en/compare";
    const { rerender } = render(<CompareBar locale="en" texts={en.compare} />);
    expect(screen.queryByRole("complementary")).toBeNull();
    pathname.value = "/en/admin/products";
    rerender(<CompareBar locale="en" texts={en.compare} />);
    expect(screen.queryByRole("complementary")).toBeNull();
  });
});

const comparison: ProductComparison = {
  products: [
    { number: 1, name: "Orvia X1", slug: "orvia-x1", imageUrl: null, price: 300, compareAtPrice: null, inStock: true, category: { name: "Phones", slug: "phones" }, brand: "Orvia" },
    { number: 2, name: "Orvia X2", slug: "orvia-x2", imageUrl: null, price: 400, compareAtPrice: null, inStock: false, category: { name: "Phones", slug: "phones" }, brand: null },
  ],
  groups: [
    {
      name: "Memory",
      rows: [
        { code: "storage", name: "Storage", kind: "Number", unit: "GB", better: "Higher", values: [{ number: 128, boolean: null, texts: [] }, { number: 256, boolean: null, texts: [] }], best: [1], differs: true },
        { code: "ram", name: "RAM", kind: "Number", unit: "GB", better: "Higher", values: [{ number: 8, boolean: null, texts: [] }, { number: 8, boolean: null, texts: [] }], best: [], differs: false },
      ],
    },
    {
      name: "Features",
      rows: [{ code: "nfc", name: "NFC", kind: "Boolean", unit: null, better: "None", values: [{ number: null, boolean: true, texts: [] }, { number: null, boolean: null, texts: [] }], best: [], differs: true }],
    },
  ],
  mixedCategories: false,
};

describe("CompareTable", () => {
  it("shows every product's price, stock and specs, marks the best value and links each value to its headers", () => {
    render(<CompareTable comparison={comparison} locale="en" money={eur} onlyDifferences={false} texts={{ compare: en.compare, product: en.product }} />);

    expect(screen.getByRole("columnheader", { name: /Orvia X1/ })).toBeTruthy();
    expect(screen.getByText("€300")).toBeTruthy();
    expect(screen.getByText(en.product.outOfStock)).toBeTruthy();
    const best = screen.getByText(en.compare.best).closest("td")!;
    expect(best.textContent).toContain("256 GB");
    expect(best.getAttribute("headers")).toBe("cmp-storage cmp-p-2");
    expect(screen.getAllByText("8 GB")).toHaveLength(2);
    expect(screen.getByText(en.compare.noValue)).toBeTruthy();
    expect(screen.getByRole("link", { name: en.compare.remove.replace("{name}", "Orvia X1") }).getAttribute("href")).toBe("/en/compare?ids=2");
    expect(screen.getByRole("link", { name: en.compare.onlyDifferences }).getAttribute("href")).toBe("/en/compare?ids=1,2&diff=1");
    expect(screen.queryByRole("note")).toBeNull();
  });

  it("hides rows where every product has the same value", () => {
    render(
      <CompareTable
        comparison={{ ...comparison, mixedCategories: true }}
        locale="en"
        money={eur}
        onlyDifferences
        texts={{ compare: en.compare, product: en.product }}
      />,
    );
    expect(screen.queryByText("RAM")).toBeNull();
    expect(screen.getByText("Storage")).toBeTruthy();
    expect(screen.getByRole("link", { name: en.compare.onlyDifferences }).getAttribute("aria-current")).toBe("page");
    expect(screen.getByRole("note").textContent).toBe(en.compare.mixed);
  });
});
