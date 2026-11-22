// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { CartLine, CartView } from "@/lib/cart/server";
import type { DisplayCurrency } from "@/lib/currency/display";
import en from "@/lib/i18n/dictionaries/en.json";
import { AddToCartButton } from "./AddToCartButton";
import { CartBadge } from "./CartBadge";
import { CartContents, shownAmount } from "./CartContents";
import { CartQuantity } from "./CartQuantity";

const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  refresh.mockReset();
  document.cookie = "os_cart_n=; path=/; max-age=0";
});

const eur: DisplayCurrency = { code: "EUR", symbol: null, decimals: 2, roundingStep: 0.01, rate: 1, isBase: true, base: "EUR" };
const line = (over: Partial<CartLine> = {}): CartLine => ({
  id: "0193a000-0000-7000-8000-000000000001",
  number: 1001,
  name: "Orvia X1",
  slug: "orvia-x1",
  variantSku: null,
  variantLabel: null,
  imageUrl: null,
  unitPrice: 100,
  compareAtPrice: null,
  quantity: 2,
  available: 5,
  lineTotal: 200,
  problem: "None",
  ...over,
});
const cart = (lines: CartLine[], over: Partial<CartView> = {}): CartView => ({
  lines,
  itemCount: lines.reduce((n, l) => n + l.quantity, 0),
  subtotal: lines.reduce((n, l) => n + l.lineTotal, 0),
  currency: "EUR",
  hasProblems: lines.some((l) => l.problem !== "None"),
  ...over,
});
const ok = (body: unknown = {}) => vi.fn(async () => new Response(JSON.stringify(body)));
const refused = () => vi.fn(async () => new Response(JSON.stringify({ code: "cart.not_enough_stock" }), { status: 409 }));

describe("CartContents", () => {
  it("lists the lines with links, variants, prices and the total", () => {
    const hoodie = line({ id: "0193a000-0000-7000-8000-000000000002", number: 1002, name: "Hoodie", slug: "hoodie", variantLabel: "Beige / M", quantity: 1, lineTotal: 39, unitPrice: 39 });
    render(<CartContents cart={cart([line(), hoodie])} locale="en" money={eur} approximate={null} texts={en.cart} errors={en.cart.errors} />);

    expect(screen.getAllByRole("link", { name: "Orvia X1" })[0].getAttribute("href")).toBe("/en/p/1001/orvia-x1");
    expect(screen.getByText("Beige / M")).toBeTruthy();
    expect(screen.getByText(en.cart.itemCount.replace("{count}", "3"))).toBeTruthy();
    expect(screen.getByText("€239")).toBeTruthy();
    expect(screen.getByText(en.cart.each.replace("{price}", "€100"))).toBeTruthy();
    // Checkout opens in the next step.
    expect((screen.getByRole("button", { name: en.cart.checkout }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("marks changed lines and says so", () => {
    const changed = cart([
      line({ problem: "NotEnoughStock", available: 1, lineTotal: 0 }),
      line({ id: "0193a000-0000-7000-8000-000000000003", problem: "Unavailable", lineTotal: 0 }),
    ]);
    render(<CartContents cart={changed} locale="en" money={eur} approximate="Approximate price; you pay in euro." texts={en.cart} errors={en.cart.errors} />);

    expect(screen.getByRole("alert").textContent).toBe(en.cart.hasProblems);
    expect(screen.getByText(en.cart.problemNotEnoughStock.replace("{count}", "1"))).toBeTruthy();
    expect(screen.getByText(en.cart.problemUnavailable)).toBeTruthy();
    expect(screen.getByText("Approximate price; you pay in euro.")).toBeTruthy();
  });

  it("shows the empty cart and clears the badge", () => {
    document.cookie = "os_cart_n=2; path=/";
    render(<CartContents cart={cart([])} locale="en" money={eur} approximate={null} texts={en.cart} errors={en.cart.errors} />);

    expect(screen.getByText(en.cart.empty)).toBeTruthy();
    expect(document.cookie).not.toContain("os_cart_n=2");
  });
});

describe("shownAmount", () => {
  it("multiplies the converted unit price, so lines and total agree with what is shown", () => {
    const usd: DisplayCurrency = { code: "USD", symbol: null, decimals: 2, roundingStep: 0.01, rate: 0.00000051594, isBase: false, base: "IRR" };
    // 349,000,000 IRR is 180.06 USD (rounded); two of them are 360.12, not the 360.13 of converting 698,000,000.
    expect(shownAmount(349_000_000, 1, usd)).toBe(180.06);
    expect(shownAmount(349_000_000, 2, usd)).toBe(360.12);
    expect(shownAmount(19.99, 3, eur)).toBe(59.97);
  });
});

describe("CartQuantity", () => {
  it("changes the quantity through the API and reloads the prices", async () => {
    const fetchMock = ok({ lines: [], itemCount: 3 });
    vi.stubGlobal("fetch", fetchMock);
    render(<CartQuantity lineId="abc" quantity={2} max={3} name="Orvia X1" texts={en.cart} errors={en.cart.errors} />);

    fireEvent.click(screen.getByRole("button", { name: en.cart.increase }));
    await vi.waitFor(() => expect(refresh).toHaveBeenCalled());
    const [path, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect([path, init.method, init.body]).toEqual(["/api/v1/cart/items/abc", "PUT", JSON.stringify({ quantity: 3 })]);
  });

  it("removes the line and never goes past the stock", async () => {
    const fetchMock = ok();
    vi.stubGlobal("fetch", fetchMock);
    render(<CartQuantity lineId="abc" quantity={3} max={3} name="Orvia X1" texts={en.cart} errors={en.cart.errors} />);

    expect((screen.getByRole("button", { name: en.cart.increase }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: en.cart.remove.replace("{name}", "Orvia X1") }));
    await vi.waitFor(() => expect(refresh).toHaveBeenCalled());
    expect((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].method).toBe("DELETE");
  });

  it("explains a refused change", async () => {
    vi.stubGlobal("fetch", refused());
    render(<CartQuantity lineId="abc" quantity={1} max={5} name="Orvia X1" texts={en.cart} errors={en.cart.errors} />);

    fireEvent.click(screen.getByRole("button", { name: en.cart.increase }));
    expect((await screen.findByRole("alert")).textContent).toBe(en.cart.errors.not_enough_stock);
    expect(refresh).not.toHaveBeenCalled();
  });
});

describe("AddToCartButton", () => {
  it("adds one piece and links to the cart", async () => {
    vi.stubGlobal("fetch", ok());
    render(<AddToCartButton locale="en" number={1001} inStock texts={en.product} errors={en.cart.errors} />);

    fireEvent.click(screen.getByRole("button", { name: en.product.addToCart }));
    expect(await screen.findByText(en.product.added)).toBeTruthy();
    expect(screen.getByRole("link", { name: en.product.viewCart }).getAttribute("href")).toBe("/en/cart");
  });

  it("says why an add was refused, and is off when sold out", async () => {
    vi.stubGlobal("fetch", refused());
    const { rerender } = render(<AddToCartButton locale="en" number={1001} inStock texts={en.product} errors={en.cart.errors} />);

    fireEvent.click(screen.getByRole("button", { name: en.product.addToCart }));
    expect(await screen.findByText(en.cart.errors.not_enough_stock)).toBeTruthy();

    rerender(<AddToCartButton locale="en" number={1001} inStock={false} texts={en.product} errors={en.cart.errors} />);
    expect((screen.getByRole("button", { name: en.product.outOfStock }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("CartBadge", () => {
  it("shows the count from the cookie and follows changes", () => {
    const { container } = render(<CartBadge locale="en" label={en.header.cartCount} />);
    expect(container.textContent).toBe("");

    act(() => {
      document.cookie = "os_cart_n=3; path=/";
      window.dispatchEvent(new Event("os-cart-changed"));
    });
    expect(screen.getByText(en.header.cartCount.replace("{count}", "3"))).toBeTruthy();
  });
});
