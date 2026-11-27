// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { OrderView } from "@/lib/account/session";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { InvoiceDocument } from "./InvoiceDocument";
import { OrderDetails } from "./OrderDetails";
import { PrintButton } from "./PrintButton";
import { ReturnRequestForm } from "./ReturnRequestForm";
import { sampleOrder } from "./orders.test";

const fetchMock = vi.fn();
beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(async () => new Response("1"));
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const delivered = (over: Partial<OrderView> = {}): OrderView =>
  sampleOrder({
    status: "Completed",
    invoice: { number: 7, issuedUtc: "2026-11-27T10:00:00Z" },
    returnableUntilUtc: "2026-12-11T10:00:00Z",
    actions: { canMarkPaid: false, canShip: false, canComplete: false, canCancel: false, canRequestReturn: true, canRefund: false },
    ...over,
  });

describe("InvoiceDocument", () => {
  it("shows the seller, the buyer, the lines and the totals", () => {
    const invoice = { number: 7, issuedUtc: "2026-11-27T10:00:00Z", sellerName: "", sellerAddress: "Valiasr 1", sellerTaxNumber: "IR-123", note: "Thank you", order: delivered({ refundedTotal: 50 }) };
    render(<InvoiceDocument invoice={invoice} locale="en" shopName="Test Shop" texts={en.orders} />);

    // An old invoice without a seller name shows the shop's.
    expect(screen.getByText("Test Shop")).toBeTruthy();
    expect(screen.getByText("IR-123")).toBeTruthy();
    expect(screen.getByText("7")).toBeTruthy();
    expect(screen.getByText("Sara Ahmadi")).toBeTruthy();
    expect(screen.getByRole("cell", { name: "DEMO-PHN-001" })).toBeTruthy();
    expect(screen.getByText(en.orders.invoiceTaxIncluded.replace("{rate}", "10"))).toBeTruthy();
    expect(screen.getByText("€185")).toBeTruthy();
    expect(screen.getByText("−€50")).toBeTruthy();
    expect(screen.getByText("Thank you")).toBeTruthy();
  });

  it("prints with the browser", () => {
    const print = vi.fn();
    vi.stubGlobal("print", print);
    render(<PrintButton label={en.orders.invoicePrint} />);
    fireEvent.click(screen.getByRole("button", { name: en.orders.invoicePrint }));
    expect(print).toHaveBeenCalled();
  });
});

describe("OrderDetails with returns", () => {
  it("links the invoice and lists returns and refunds", () => {
    const order = delivered({
      returns: [
        {
          id: "r1",
          number: 3,
          status: "Approved",
          createdUtc: "2026-11-28T10:00:00Z",
          reason: "Too big",
          answer: "Send it to Valiasr 1",
          lines: [{ orderItemId: "i1", name: "Orvia X1", variantLabel: null, sku: "DEMO-PHN-001", quantity: 1 }],
          suggestedRefund: 90,
          canApprove: false,
          canReject: true,
          canReceive: true,
        },
      ],
      refunds: [{ atUtc: "2026-11-29T10:00:00Z", amount: 90, method: "Card", note: "Ref 42", returnNumber: 3 }],
      refundedTotal: 90,
      history: [{ atUtc: "2026-11-29T10:00:00Z", kind: "Refunded", status: null, text: "90", isInternal: false, byStaff: true }],
    });
    render(<OrderDetails order={order} locale="en" texts={en.orders} invoiceHref="/en/account/orders/1001/invoice" />);

    expect(screen.getByRole("link", { name: "Invoice 7" }).getAttribute("href")).toBe("/en/account/orders/1001/invoice");
    expect(screen.getByText(en.orders.returnStatus.Approved)).toBeTruthy();
    expect(screen.getByText("Send it to Valiasr 1")).toBeTruthy();
    expect(screen.getByText("Refunded €90")).toBeTruthy();
    // The staff note and the suggested refund are for staff only.
    expect(screen.queryByText(/Ref 42/)).toBeNull();
    expect(screen.queryByText(/Suggested refund/)).toBeNull();
  });
});

describe("ReturnRequestForm", () => {
  it("sends the chosen quantities and the reason", async () => {
    render(<ReturnRequestForm order={delivered()} locale="en" endpoint="/api/v1/orders/1001/returns?key=k" texts={en.orders} errors={en.orders.errors} />);
    expect(screen.getByText(/until Dec 11, 2026/)).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: en.orders.returnStart }));
    fireEvent.change(screen.getByLabelText("Orvia X1"), { target: { value: "1" } });
    fireEvent.change(screen.getByLabelText(en.orders.returnReason), { target: { value: "Too big" } });
    fireEvent.click(screen.getByRole("button", { name: en.orders.returnSend }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/orders/1001/returns?key=k");
    expect(JSON.parse(String(init.body))).toEqual({ lines: [{ orderItemId: "i1", quantity: 1 }], reason: "Too big" });
  });

  it("explains a refusal", async () => {
    fetchMock.mockImplementation(async () => new Response(JSON.stringify({ code: "order.return_not_allowed" }), { status: 409 }));
    render(<ReturnRequestForm order={delivered()} locale="en" endpoint="/api/v1/account/orders/1001/returns" texts={en.orders} errors={en.orders.errors} />);
    fireEvent.click(screen.getByRole("button", { name: en.orders.returnStart }));
    fireEvent.change(screen.getByLabelText("Orvia X1"), { target: { value: "2" } });
    fireEvent.change(screen.getByLabelText(en.orders.returnReason), { target: { value: "Broken" } });
    fireEvent.click(screen.getByRole("button", { name: en.orders.returnSend }));
    expect(await screen.findByText(en.orders.errors.return_not_allowed)).toBeTruthy();
  });
});
