// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import type { ProductCard as ProductCardData, ProductDetail } from "@/lib/api/storefront";
import type { DisplayCurrency } from "@/lib/currency/display";

vi.mock("next/link", () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), refresh: vi.fn() }) }));
// The wishlist button asks once per page; a guest here.
vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ numbers: [], signedIn: false }), { status: 200 })));
vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt, className }: { src: string; alt: string; className?: string }) => <img src={src} alt={alt} className={className} />,
}));

import { Price } from "./Price";
import { ProductCard } from "./ProductCard";
import { ProductRail } from "./ProductRail";
import { ProductView } from "./ProductView";
import { SpecTable } from "./SpecTable";

afterEach(cleanup);

const eur: DisplayCurrency = { code: "EUR", symbol: null, decimals: 2, roundingStep: 0.01, rate: 1, isBase: true, base: "EUR" };
const toman: DisplayCurrency = { code: "IRT", symbol: "Toman", decimals: 0, roundingStep: 1000, rate: 100_000, isBase: false, base: "EUR" };

const card = (over: Partial<ProductCardData> = {}): ProductCardData => ({
  number: 1001,
  name: "Orvia X1",
  slug: "orvia-x1",
  imageUrl: "/demo/products/phone-x1.svg",
  price: 349,
  compareAtPrice: 399,
  inStock: true,
  categorySlug: "phones",
  ratingAverage: 4.5,
  ratingCount: 2,
  ...over,
});

const detail: ProductDetail = {
  ...card(),
  description: "A bright display.",
  sku: "DEMO-PHN-001",
  stock: 4,
  images: [{ url: "/demo/products/phone-x1.svg", alt: null }],
  category: { name: "Electronics", slug: "electronics" },
  subCategory: { name: "Mobile phones", slug: "mobile-phones" },
  specs: [
    { code: "brand", group: "General", name: "Brand", kind: "Text", unit: null, number: null, boolean: null, value: "Orvia", inSummary: true },
    { code: "screen_size", group: "Display", name: "Screen size", kind: "Number", unit: "in", number: 6.5, boolean: null, value: null, inSummary: true },
    { code: "network_5g", group: "Features", name: "5G support", kind: "Boolean", unit: null, number: null, boolean: true, value: null, inSummary: false },
  ],
  alternates: [{ locale: "en", slug: "orvia-x1" }],
  brand: null,
  variantAxes: [],
  variants: [],
  gtin: null,
  mpn: null,
  weightGrams: null,
  metaTitle: null,
  metaDescription: null,
  videos: [],
  ratingAverage: 0,
  ratingCount: 0,
};

describe("Price", () => {
  it("shows the discount and the struck-out old price", () => {
    render(<Price price={80} compareAtPrice={100} money={eur} locale="en" texts={en.product} />);

    expect(screen.getByLabelText("20% off").textContent).toBe("20%");
    expect(screen.getByLabelText(en.product.oldPrice).textContent).toBe("€100");
    expect(screen.getByText("€80")).toBeTruthy();
  });

  it("converts both prices to the display currency (the discount share stays the same)", () => {
    render(<Price price={80} compareAtPrice={100} money={toman} locale="en" texts={en.product} />);

    expect(screen.getByText("8,000,000 Toman")).toBeTruthy();
    expect(screen.getByLabelText(en.product.oldPrice).textContent).toBe("10,000,000 Toman");
    expect(screen.getByLabelText("20% off")).toBeTruthy();
  });

  it("shows only the price without a discount", () => {
    render(<Price price={80} money={eur} locale="en" texts={en.product} />);

    expect(screen.queryByLabelText(en.product.oldPrice)).toBeNull();
  });
});

describe("ProductCard", () => {
  it("links to the product page and marks products out of stock", () => {
    render(<ProductCard product={card({ inStock: false })} locale="en" money={eur} texts={en.product} />);

    const link = screen.getByRole("link");
    expect(link.getAttribute("href")).toBe("/en/p/1001/orvia-x1");
    expect(within(link).getByText("Orvia X1")).toBeTruthy();
    expect(within(link).getByText(en.product.outOfStock)).toBeTruthy();
  });
});

describe("ProductRail", () => {
  it("lists the products under a heading", () => {
    render(
      <ProductRail id="deals" heading="Special offers" products={[card(), card({ number: 1002, slug: "b" })]} locale="en" money={eur} texts={en.product} variant="deals" />,
    );

    const section = screen.getByRole("region", { name: "Special offers" });
    expect(within(section).getAllByRole("link").map((l) => l.getAttribute("href"))).toEqual(["/en/p/1001/orvia-x1", "/en/p/1002/b"]);
  });

  it("renders nothing for an empty list", () => {
    const { container } = render(<ProductRail id="x" heading="X" products={[]} locale="en" money={eur} texts={en.product} />);

    expect(container.innerHTML).toBe("");
  });
});

describe("SpecTable", () => {
  it("groups specs and formats each value", () => {
    render(<SpecTable specs={detail.specs} locale="en" texts={en.product} heading="Specifications" />);

    const table = screen.getByRole("region", { name: "Specifications" });
    expect(within(table).getByRole("heading", { level: 3, name: "Display" })).toBeTruthy();
    expect(within(table).getByText("6.5 in")).toBeTruthy();
    expect(within(table).getByText(en.product.yes)).toBeTruthy();
  });
});

describe("ProductView", () => {
  it("shows the heading, key facts, price box, description and specs", () => {
    render(<ProductView product={detail} locale="en" money={eur} baseName="euro" texts={en.product} />);

    expect(screen.getByRole("heading", { level: 1, name: "Orvia X1" })).toBeTruthy();
    expect(screen.getByRole("navigation", { name: en.product.breadcrumb }).textContent).toContain("Mobile phones");
    const facts = screen.getByRole("region", { name: en.product.keyFacts });
    expect(within(facts).getAllByRole("listitem")).toHaveLength(2);
    expect(screen.getByText(en.product.inStock)).toBeTruthy();
    const button = screen.getByRole("button", { name: en.product.addToCart }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(screen.getByText("A bright display.")).toBeTruthy();
    expect(screen.getByRole("img", { name: "Orvia X1" })).toBeTruthy();
    expect(screen.queryByText(/Approximate price/)).toBeNull();
  });

  it("says a converted price is approximate", () => {
    render(<ProductView product={detail} locale="en" money={toman} baseName="euro" texts={en.product} />);

    expect(screen.getByText("Approximate price; you pay in euro.")).toBeTruthy();
  });

  it("names the brand and puts the variant choice in the price box", () => {
    const withVariants: ProductDetail = {
      ...detail,
      brand: { name: "Orvia Inc.", slug: "orvia", logoUrl: null },
      variantAxes: [{ code: "size", name: "Size", options: [{ code: "m", value: "M" }] }],
      variants: [{ sku: "X1-M", gtin: null, price: 300, compareAtPrice: null, inStock: true, options: [{ axis: "size", option: "m" }] }],
    };
    render(<ProductView product={withVariants} locale="en" money={eur} baseName="euro" texts={en.product} />);

    expect(screen.getByText("Orvia Inc.")).toBeTruthy();
    // The brand itself replaces the old "brand" spec in the key facts.
    expect(within(screen.getByRole("region", { name: en.product.keyFacts })).getAllByRole("listitem")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "M" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByText("X1-M")).toBeTruthy();
    expect(screen.queryByText("DEMO-PHN-001")).toBeNull();
  });
});
