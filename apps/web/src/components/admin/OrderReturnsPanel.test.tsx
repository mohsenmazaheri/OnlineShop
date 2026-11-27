// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { OrderView } from "@/lib/account/session";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { OrderReturnsPanel } from "./OrderReturnsPanel";

const fetchMock = vi.fn();
beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(async () => new Response(JSON.stringify("Approved")));
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const request = (over: Partial<OrderView["returns"][number]> = {}): OrderView["returns"][number] => ({
  id: "r1",
  number: 3,
  status: "Requested",
  createdUtc: "2026-11-28T10:00:00Z",
  reason: "Too big",
  answer: null,
  lines: [],
  suggestedRefund: 90,
  canApprove: true,
  canReject: true,
  canReceive: false,
  ...over,
});

const order = (over: Partial<OrderView>) =>
  ({ number: 1001, currency: "EUR", refundable: 185, returns: [], actions: { canRefund: false }, ...over }) as unknown as OrderView;

const call = (n: number) => {
  const [url, init] = fetchMock.mock.calls[n] as unknown as [string, RequestInit];
  return { url, body: JSON.parse(String(init.body)) };
};

describe("OrderReturnsPanel", () => {
  it("answers a return", async () => {
    render(<OrderReturnsPanel order={order({ returns: [request()] })} texts={en.admin} errors={en.orders.errors} canManage />);
    expect(screen.queryByRole("button", { name: en.admin.returnReceive })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: en.admin.returnApprove }));
    fireEvent.change(screen.getByLabelText(en.admin.returnAnswer), { target: { value: "Send it to Valiasr 1" } });
    fireEvent.click(screen.getAllByRole("button", { name: en.admin.returnApprove }).at(-1)!);
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call(0)).toEqual({ url: "/api/v1/admin/orders/1001/returns/r1/approve", body: { text: "Send it to Valiasr 1" } });
  });

  it("records a refund for a received return, suggesting its amount", async () => {
    fetchMock.mockImplementation(async () => new Response("90"));
    const received = request({ status: "Received", canApprove: false, canReject: false });
    render(<OrderReturnsPanel order={order({ returns: [received], actions: { canRefund: true } as OrderView["actions"] })} texts={en.admin} errors={en.orders.errors} canManage />);

    fireEvent.click(screen.getByRole("button", { name: en.admin.refundRecord }));
    expect((screen.getByLabelText(`${en.admin.refundAmount} (EUR)`) as HTMLInputElement).value).toBe("90");
    fireEvent.change(screen.getByLabelText(en.admin.refundMethod), { target: { value: "Card" } });
    fireEvent.click(screen.getAllByRole("button", { name: en.admin.refundRecord }).at(-1)!);
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call(0)).toEqual({ url: "/api/v1/admin/orders/1001/refunds", body: { amount: 90, method: "Card", note: null, returnId: "r1" } });
  });

  it("shows nothing without returns, refunds or orders.manage", () => {
    const { container, rerender } = render(<OrderReturnsPanel order={order({})} texts={en.admin} errors={en.orders.errors} canManage />);
    expect(container.textContent).toBe("");
    rerender(<OrderReturnsPanel order={order({ returns: [request()] })} texts={en.admin} errors={en.orders.errors} canManage={false} />);
    expect(container.textContent).toBe("");
  });
});
