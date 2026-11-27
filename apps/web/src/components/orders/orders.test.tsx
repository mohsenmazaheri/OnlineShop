// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import type { OrderPage, OrderView } from "@/lib/account/session";
import en from "@/lib/i18n/dictionaries/en.json";
import { OrderDetails } from "./OrderDetails";
import { OrderList } from "./OrderList";

afterEach(cleanup);

export const sampleOrder = (over: Partial<OrderView> = {}): OrderView => ({
  number: 1001,
  status: "Shipped",
  orderedUtc: "2026-11-27T09:00:00Z",
  paymentDueUtc: null,
  locale: "en",
  email: "sara@example.com",
  recipientName: "Sara Ahmadi",
  phoneNumber: "+989121234567",
  address: { countryCode: "IR", region: null, city: "Tehran", line1: "Valiasr 10", line2: null, postalCode: null },
  isPickup: false,
  shippingMethodName: "Express post",
  trackingCarrier: "Post",
  trackingCode: "RR123",
  note: "Call first",
  discountCode: "WELCOME",
  currency: "EUR",
  subtotal: 200,
  discount: 20,
  shipping: 5,
  tax: 16.82,
  taxRate: 10,
  pricesIncludeTax: true,
  total: 185,
  lines: [{ id: "i1", number: 1001, name: "Orvia X1", sku: "DEMO-PHN-001", variantLabel: null, imageUrl: null, unitPrice: 100, quantity: 2, lineTotal: 200, returnable: 2 }],
  history: [
    { atUtc: "2026-11-27T09:00:00Z", kind: "Placed", status: null, text: null, isInternal: false, byStaff: false },
    { atUtc: "2026-11-27T10:00:00Z", kind: "StatusChanged", status: "Paid", text: null, isInternal: false, byStaff: true },
    { atUtc: "2026-11-27T10:05:00Z", kind: "Note", status: null, text: "Packed by Ali", isInternal: true, byStaff: true },
  ],
  actions: { canMarkPaid: false, canShip: false, canComplete: true, canCancel: false, canRequestReturn: false, canRefund: false },
  customerId: null,
  invoice: null,
  returns: [],
  refunds: [],
  refundedTotal: 0,
  refundable: 0,
  returnableUntilUtc: null,
  ...over,
});

describe("OrderDetails", () => {
  it("shows the lines, amounts, delivery with tracking and the history", () => {
    render(<OrderDetails order={sampleOrder()} locale="en" texts={en.orders} />);

    expect(screen.getByRole("link", { name: "Orvia X1" }).getAttribute("href")).toBe("/en/p/1001");
    expect(screen.getByText("Discount (WELCOME)")).toBeTruthy();
    expect(screen.getByText("€185")).toBeTruthy();
    expect(screen.getByText(en.orders.taxIncluded.replace("{amount}", "€16.82").replace("{rate}", "10"))).toBeTruthy();
    expect(screen.getByText("RR123")).toBeTruthy();
    expect(screen.getByText(/Valiasr 10/)).toBeTruthy();
    const history = screen.getByRole("region", { name: en.orders.historyTitle });
    expect(within(history).getByText(en.orders.historyPlaced)).toBeTruthy();
    expect(within(history).getByText("Status: Paid")).toBeTruthy();
    // Customers never see who acted; the email stays out of the customer's own view.
    expect(screen.queryByText(en.orders.byStaff, { exact: false })).toBeNull();
    expect(screen.queryByText("sara@example.com")).toBeNull();
  });

  it("shows staff the internal notes and who acted, and the payment deadline while waiting", () => {
    render(<OrderDetails order={sampleOrder({ status: "Pending", paymentDueUtc: "2026-11-27T09:15:00Z", trackingCode: null })} locale="en" texts={en.orders} staff />);
    expect(screen.getAllByText(en.orders.internal).length).toBe(1);
    expect(screen.getAllByText(new RegExp(en.orders.byStaff)).length).toBe(2);
    expect(screen.getByText("sara@example.com")).toBeTruthy();
    expect(screen.getByText(/Waiting for payment/)).toBeTruthy();
  });

  it("shows pickup orders without an address", () => {
    render(<OrderDetails order={sampleOrder({ isPickup: true, address: null, trackingCode: null })} locale="en" texts={en.orders} />);
    expect(screen.getByText(en.orders.pickup)).toBeTruthy();
    expect(screen.queryByText(/Valiasr/)).toBeNull();
  });
});

describe("OrderList", () => {
  const page = (over: Partial<OrderPage> = {}): OrderPage => ({
    items: [{ number: 1002, status: "Pending", orderedUtc: "2026-11-27T09:00:00Z", recipientName: "Sara", email: null, itemCount: 3, total: 50, currency: "EUR", imageUrl: null }],
    total: 45,
    page: 2,
    pageSize: 20,
    ...over,
  });

  it("links each order and pages through the list", () => {
    render(<OrderList page={page()} locale="en" texts={en.orders} hrefFor={(n) => `/en/account/orders/${n}`} pageHref={(p) => `/en/account/orders?page=${p}`} />);
    expect(screen.getByRole("link", { name: /Order 1002/ }).getAttribute("href")).toBe("/en/account/orders/1002");
    expect(screen.getByText(en.orders.status.Pending)).toBeTruthy();
    expect(screen.getByRole("link", { name: en.orders.newer }).getAttribute("href")).toBe("/en/account/orders?page=1");
    expect(screen.getByRole("link", { name: en.orders.older }).getAttribute("href")).toBe("/en/account/orders?page=3");
    expect(screen.getByText("Page 2 of 3")).toBeTruthy();
  });

  it("says when there are no orders", () => {
    render(<OrderList page={page({ items: [], total: 0, page: 1 })} locale="en" texts={en.orders} hrefFor={() => ""} pageHref={() => ""} />);
    expect(screen.getByText(en.orders.empty)).toBeTruthy();
  });
});
