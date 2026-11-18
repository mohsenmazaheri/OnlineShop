// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import type { ProductCard as ProductCardData, ProductDetail } from "@/lib/api/storefront";

vi.mock("next/link", () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));
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

const card = (over: Partial<ProductCardData> = {}): ProductCardData => ({
  number: 1001,
  name: "Orvia X1",
  slug: "orvia-x1",
  imageUrl: "/demo/products/phone-x1.svg",
  price: 349,
  compareAtPrice: 399,
  inStock: true,
  ...over,
});

const detail: ProductDetail = {
  ...card(),
  description: "A bright display.",
  sku: "DEMO-PHN-001",
  stock: 4,
  images: ["/demo/products/phone-x1.svg"],
  category: { name: "Electronics", slug: "electronics" },
  subCategory: { name: "Mobile phones", slug: "mobile-phones" },
  specs: [
    { code: "brand", group: "General", name: "Brand", kind: "Text", unit: null, number: null, boolean: null, value: "Orvia", inSummary: true },
    { code: "screen_size", group: "Display", name: "Screen size", kind: "Number", unit: "in", number: 6.5, boolean: null, value: null, inSummary: true },
    { code: "network_5g", group: "Features", name: "5G support", kind: "Boolean", unit: null, number: null, boolean: true, value: null, inSummary: false },
  ],
  alternates: [{ locale: "en", slug: "orvia-x1" }],
};

describe("Price", () => {
  it("shows the discount and the struck-out old price", () => {
    render(<Price price={80} compareAtPrice={100} currency="EUR" locale="en" texts={en.product} />);

    expect(screen.getByLabelText("20% off").textContent).toBe("20%");
    expect(screen.getByLabelText(en.product.oldPrice).textContent).toBe("€100");
    expect(screen.getByText("€80")).toBeTruthy();
  });

  it("shows only the price without a discount", () => {
    render(<Price price={80} currency="EUR" locale="en" texts={en.product} />);

    expect(screen.queryByLabelText(en.product.oldPrice)).toBeNull();
  });
});

describe("ProductCard", () => {
  it("links to the product page and marks products out of stock", () => {
    render(<ProductCard product={card({ inStock: false })} locale="en" currency="EUR" texts={en.product} />);

    const link = screen.getByRole("link");
    expect(link.getAttribute("href")).toBe("/en/p/1001/orvia-x1");
    expect(within(link).getByText("Orvia X1")).toBeTruthy();
    expect(within(link).getByText(en.product.outOfStock)).toBeTruthy();
  });
});

describe("ProductRail", () => {
  it("lists the products under a heading", () => {
    render(
      <ProductRail id="deals" heading="Special offers" products={[card(), card({ number: 1002, slug: "b" })]} locale="en" currency="EUR" texts={en.product} variant="deals" />,
    );

    const section = screen.getByRole("region", { name: "Special offers" });
    expect(within(section).getAllByRole("link").map((l) => l.getAttribute("href"))).toEqual(["/en/p/1001/orvia-x1", "/en/p/1002/b"]);
  });

  it("renders nothing for an empty list", () => {
    const { container } = render(<ProductRail id="x" heading="X" products={[]} locale="en" currency="EUR" texts={en.product} />);

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
    render(<ProductView product={detail} locale="en" currency="EUR" texts={en.product} />);

    expect(screen.getByRole("heading", { level: 1, name: "Orvia X1" })).toBeTruthy();
    expect(screen.getByRole("navigation", { name: en.product.breadcrumb }).textContent).toContain("Mobile phones");
    const facts = screen.getByRole("region", { name: en.product.keyFacts });
    expect(within(facts).getAllByRole("listitem")).toHaveLength(2);
    expect(screen.getByText(en.product.inStock)).toBeTruthy();
    const button = screen.getByRole("button", { name: en.product.addToCart }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(screen.getByText("A bright display.")).toBeTruthy();
    expect(screen.getByRole("img", { name: "Orvia X1" })).toBeTruthy();
  });
});
