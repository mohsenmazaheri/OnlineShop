// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { CheckoutQuote, PlacedOrder } from "@/lib/checkout/form";
import type { CheckoutView } from "@/lib/checkout/server";
import type { DisplayCurrency } from "@/lib/currency/display";
import en from "@/lib/i18n/dictionaries/en.json";
import { CheckoutForm } from "./CheckoutForm";
import { CheckoutPlaced } from "./CheckoutPlaced";

const eur: DisplayCurrency = { code: "EUR", symbol: null, decimals: 2, roundingStep: 0.01, rate: 1, isBase: true, base: "EUR" };
const errors = { ...en.account.errors, ...en.checkout.errors };

const post = { id: "m-post", name: "Post", description: "Tracked", isPickup: false, countries: [], minDays: 2, maxDays: 4, cost: 5 };
const shop = { id: "m-shop", name: "Pickup", description: null, isPickup: true, countries: [], minDays: null, maxDays: null, cost: 0 };

const view = (over: Partial<CheckoutView> = {}): CheckoutView => ({
  cart: {
    lines: [
      {
        id: "l1",
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
      },
    ],
    itemCount: 2,
    subtotal: 200,
    currency: "EUR",
    hasProblems: false,
  },
  signedIn: false,
  guestCheckout: true,
  emailRequired: true,
  contact: null,
  addresses: [],
  countries: ["IR", "DE"],
  shippingMethods: [post, shop],
  tax: { rate: 10, pricesIncludeTax: true },
  ...over,
});

const quote = (over: Partial<CheckoutQuote> = {}): CheckoutQuote => ({
  totals: { subtotal: 200, discount: 0, shipping: 5, tax: 18.64, total: 205, taxRate: 10, pricesIncludeTax: true },
  currency: "EUR",
  shippingMethods: [post, shop],
  shippingMethodId: "m-post",
  discountApplied: false,
  discountError: null,
  hasProblems: false,
  ...over,
});

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const fetchMock = vi.fn();
beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("scrollTo", () => {});
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const renderForm = (v = view()) =>
  render(<CheckoutForm locale="en" view={v} money={eur} texts={en.checkout} addressTexts={en.account} errors={errors} signInHref="/en/account/login?returnUrl=%2Fen%2Fcheckout" />);

const bodyOf = (call: number) => JSON.parse(String((fetchMock.mock.calls[call] as unknown as [string, RequestInit])[1].body));

describe("CheckoutForm", () => {
  it("quotes the choices and shows the totals with the tax inside", async () => {
    fetchMock.mockImplementation(async () => json(quote()));
    renderForm();

    expect(await screen.findByText("€205")).toBeTruthy();
    expect(bodyOf(0)).toEqual({ countryCode: "IR", shippingMethodId: "m-post", discountCode: null });
    expect(screen.getByText(en.checkout.taxIncluded.replace("{amount}", "€18.64").replace("{rate}", "10"))).toBeTruthy();
    expect(screen.getByText("Tracked · 2–4 working days")).toBeTruthy();
    expect(screen.getByRole("link", { name: en.checkout.signIn }).getAttribute("href")).toContain("returnUrl");
  });

  it("asks for the pickup contact instead of an address", async () => {
    fetchMock.mockImplementation(async () => json(quote()));
    renderForm();
    await screen.findByText("€205");

    fireEvent.click(screen.getByRole("radio", { name: /Pickup/ }));
    await waitFor(() => expect(screen.getByLabelText(en.checkout.pickupName)).toBeTruthy());
    expect(screen.queryByLabelText(en.account.city)).toBeNull();
  });

  it("places the order with the total shown and confirms it", async () => {
    const placed: PlacedOrder = { number: 1001, total: 205, currency: "EUR", paymentDueUtc: "2026-11-22T10:15:00Z", email: "a@example.com" };
    fetchMock.mockImplementation(async (url: string) => (url.includes("/orders") ? json(placed) : json(quote())));
    renderForm();
    await screen.findByText("€205");

    fireEvent.change(screen.getByLabelText(en.checkout.email), { target: { value: "a@example.com" } });
    fireEvent.change(screen.getByLabelText(en.account.recipientName), { target: { value: "Sara" } });
    fireEvent.change(screen.getByLabelText(en.account.recipientPhone), { target: { value: "09121234567" } });
    fireEvent.change(screen.getByLabelText(en.account.city), { target: { value: "Tehran" } });
    fireEvent.change(screen.getByLabelText(en.account.line1), { target: { value: "Valiasr 10" } });
    fireEvent.click(screen.getByRole("button", { name: en.checkout.place }));

    expect(await screen.findByText(en.checkout.placedTitle)).toBeTruthy();
    const call = fetchMock.mock.calls.findIndex(([url]) => String(url).includes("/orders"));
    expect(bodyOf(call)).toMatchObject({
      shippingMethodId: "m-post",
      addressId: null,
      address: { recipientName: "Sara", countryCode: "IR", city: "Tehran", line1: "Valiasr 10" },
      email: "a@example.com",
      expectedTotal: 205,
    });
  });

  it("uses a saved address and explains a refused order", async () => {
    fetchMock.mockImplementation(async (url: string) =>
      url.includes("/orders") ? json({ code: "checkout.total_changed" }, 409) : json(quote()),
    );
    renderForm(
      view({
        signedIn: true,
        emailRequired: false,
        contact: { name: "Sara", email: "sara@example.com", phoneNumber: null },
        addresses: [
          { id: "a1", label: "Home", recipientName: "Sara", phoneNumber: "+989121234567", countryCode: "IR", region: null, city: "Shiraz", line1: "Zand 5", line2: null, postalCode: null, isDefault: true },
        ],
      }),
    );
    await screen.findByText("€205");
    expect(screen.getByText(en.checkout.signedInAs.replace("{name}", "sara@example.com"))).toBeTruthy();
    expect(screen.queryByLabelText(en.account.city)).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: en.checkout.place }));
    expect(await screen.findByText(en.checkout.errors.total_changed)).toBeTruthy();
    const call = fetchMock.mock.calls.findIndex(([url]) => String(url).includes("/orders"));
    expect(bodyOf(call)).toMatchObject({ addressId: "a1", address: null });
  });

  it("applies a discount code and reports a refused one", async () => {
    fetchMock.mockImplementation(async (_url: string, init: RequestInit) => {
      const code = JSON.parse(String(init.body)).discountCode;
      if (code === "WELCOME") return json(quote({ discountApplied: true, totals: { ...quote().totals, discount: 20, total: 185 } }));
      if (code) return json(quote({ discountError: "checkout.discount_invalid" }));
      return json(quote());
    });
    renderForm();
    await screen.findByText("€205");

    fireEvent.change(screen.getByLabelText(en.checkout.discountTitle), { target: { value: "nope" } });
    fireEvent.click(screen.getByRole("button", { name: en.checkout.discountApply }));
    expect(await screen.findByText(en.checkout.errors.discount_invalid)).toBeTruthy();

    fireEvent.change(screen.getByLabelText(en.checkout.discountTitle), { target: { value: "WELCOME" } });
    fireEvent.click(screen.getByRole("button", { name: en.checkout.discountApply }));
    expect(await screen.findByText("€185")).toBeTruthy();
    expect(screen.getByText(en.checkout.discountApplied.replace("{code}", "WELCOME"))).toBeTruthy();
  });
});

describe("CheckoutPlaced", () => {
  it("shows the number, the amount and the hold time", () => {
    render(<CheckoutPlaced locale="en" order={{ number: 1001, total: 205, currency: "EUR", paymentDueUtc: "2026-11-22T10:15:00Z", email: null }} money={eur} texts={en.checkout} />);
    expect(screen.getByText("1001")).toBeTruthy();
    expect(screen.getByText(en.checkout.placedTotal.replace("{total}", "€205"))).toBeTruthy();
    expect(screen.getByRole("link", { name: en.checkout.continueShopping }).getAttribute("href")).toBe("/en");
  });
});
