// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { DiscountCodeAdmin, ShippingMethodAdmin } from "@/lib/account/session";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { DiscountsEditor, dayOf, discountSummary, utcOf } from "./DiscountsEditor";
import { ShippingEditor, optionalNumber } from "./ShippingEditor";

const fetchMock = vi.fn();
beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(async () => new Response("{}"));
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("confirm", () => true);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const bodyOf = (call: number) => {
  const [url, init] = fetchMock.mock.calls[call] as unknown as [string, RequestInit];
  return { url, method: init.method, body: init.body ? JSON.parse(String(init.body)) : undefined };
};

const method: ShippingMethodAdmin = {
  id: "m1",
  texts: [
    { locale: "fa", name: "پست", slug: null, description: null },
    { locale: "en", name: "Post", slug: null, description: null },
  ],
  isPickup: false,
  cost: 5,
  costPerKg: 1,
  freeOver: 100,
  countries: ["DE"],
  minDays: 2,
  maxDays: 4,
  isActive: true,
  sortOrder: 0,
};

const renderShipping = (methods: ShippingMethodAdmin[] = [method]) =>
  render(
    <ShippingEditor
      locale="en"
      texts={en.admin}
      methods={methods}
      tax={{ rate: 9, pricesIncludeTax: true }}
      locales={["fa", "en"]}
      defaultLocale="fa"
      countries={["IR", "DE"]}
      currency="EUR"
      languageNames={{ fa: "فارسی", en: "English" }}
    />,
  );

describe("helpers", () => {
  it("read numbers, days and summaries", () => {
    const form = new FormData();
    form.set("a", " 12.5 ");
    form.set("b", "");
    expect([optionalNumber(form, "a"), optionalNumber(form, "b"), optionalNumber(form, "c")]).toEqual([12.5, null, null]);
    expect([dayOf("2026-03-01T00:00:00Z"), dayOf(null)]).toEqual(["2026-03-01", ""]);
    expect([utcOf("2026-03-01"), utcOf("2026-03-31", true), utcOf("")]).toEqual(["2026-03-01T00:00:00Z", "2026-03-31T23:59:59Z", null]);
    expect(discountSummary({ kind: "Percent", value: 10, maxDiscount: 5 }, "EUR", "en", en.admin)).toBe("10% (at most 5 EUR)");
    expect(discountSummary({ kind: "Amount", value: 5, maxDiscount: null }, "EUR", "en", en.admin)).toBe("5 EUR");
    expect(discountSummary({ kind: "FreeShipping", value: 0, maxDiscount: null }, "EUR", "en", en.admin)).toBe(en.admin.discountKindFreeShipping);
  });
});

describe("ShippingEditor", () => {
  it("saves the tax rate", async () => {
    renderShipping();
    fireEvent.change(screen.getByLabelText(en.admin.taxRate), { target: { value: "10" } });
    fireEvent.click(screen.getByRole("button", { name: en.admin.taxSave }));
    expect(await screen.findByText(en.admin.taxSaved)).toBeTruthy();
    expect(bodyOf(0)).toEqual({ url: "/api/v1/admin/shipping/tax", method: "PUT", body: { rate: 10, pricesIncludeTax: true } });
  });

  it("edits a method and turns it into pickup", async () => {
    renderShipping();
    fireEvent.click(screen.getByRole("button", { name: /پست/ }));
    fireEvent.click(screen.getByLabelText(en.admin.methodPickupLabel));
    fireEvent.click(screen.getByRole("button", { name: en.admin.methodSave }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());

    const { url, method: verb, body } = bodyOf(0);
    expect([url, verb]).toEqual(["/api/v1/admin/shipping/m1", "PUT"]);
    expect(body).toMatchObject({ isPickup: true, cost: 5, costPerKg: 0, freeOver: 100, countries: [], minDays: 2, maxDays: 4, isActive: true });
    expect(body.texts).toEqual([
      { locale: "fa", name: "پست", slug: null, description: null },
      { locale: "en", name: "Post", slug: null, description: null },
    ]);
  });

  it("starts with a new method form when there is none and shows refusals", async () => {
    fetchMock.mockImplementation(async () => new Response(JSON.stringify({ code: "shipping.country_invalid" }), { status: 400 }));
    renderShipping([]);
    expect(screen.getByText(en.admin.methodEmpty)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: en.admin.methodSave }));
    expect(await screen.findByText(en.admin.shippingErrors.country_invalid)).toBeTruthy();
    expect(bodyOf(0)).toMatchObject({ url: "/api/v1/admin/shipping", method: "POST" });
  });
});

describe("DiscountsEditor", () => {
  const code: DiscountCodeAdmin = {
    id: "d1",
    code: "WELCOME",
    kind: "Percent",
    value: 10,
    minSubtotal: null,
    maxDiscount: 5,
    startsUtc: "2026-03-01T00:00:00Z",
    endsUtc: null,
    usageLimit: 100,
    usedCount: 3,
    oncePerCustomer: true,
    isActive: true,
    note: null,
  };

  it("lists the codes with their use and saves a changed one", async () => {
    render(<DiscountsEditor locale="en" texts={en.admin} codes={[code]} currency="EUR" />);
    expect(screen.getByText(/used 3 of 100/)).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: /WELCOME/ }));
    fireEvent.change(screen.getByLabelText(en.admin.discountKind), { target: { value: "FreeShipping" } });
    expect(screen.queryByLabelText(en.admin.discountPercent)).toBeNull();
    fireEvent.change(screen.getByLabelText(en.admin.discountEnds), { target: { value: "2026-03-31" } });
    fireEvent.click(screen.getByRole("button", { name: en.admin.discountSave }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());

    expect(bodyOf(0)).toEqual({
      url: "/api/v1/admin/discounts/d1",
      method: "PUT",
      body: {
        code: "WELCOME",
        kind: "FreeShipping",
        value: 0,
        minSubtotal: null,
        maxDiscount: null,
        startsUtc: "2026-03-01T00:00:00Z",
        endsUtc: "2026-03-31T23:59:59Z",
        usageLimit: 100,
        oncePerCustomer: true,
        isActive: true,
        note: null,
      },
    });
  });

  it("deletes a code after asking", async () => {
    render(<DiscountsEditor locale="en" texts={en.admin} codes={[code]} currency="EUR" />);
    fireEvent.click(screen.getByRole("button", { name: /WELCOME/ }));
    fireEvent.click(screen.getByRole("button", { name: en.admin.discountDelete }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(bodyOf(0)).toMatchObject({ url: "/api/v1/admin/discounts/d1", method: "DELETE" });
  });
});
