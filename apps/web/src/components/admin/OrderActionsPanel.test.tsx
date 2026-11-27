// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { OrderView } from "@/lib/account/session";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { OrderActionsPanel } from "./OrderActionsPanel";

const fetchMock = vi.fn();
beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(async () => new Response(JSON.stringify("Paid")));
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("confirm", () => true);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const order = (actions: Partial<OrderView["actions"]>) =>
  ({ number: 1001, actions: { canMarkPaid: false, canShip: false, canComplete: false, canCancel: false, ...actions } }) as OrderView;

const call = (n: number) => {
  const [url, init] = fetchMock.mock.calls[n] as unknown as [string, RequestInit];
  return { url, body: JSON.parse(String(init.body)) };
};

describe("OrderActionsPanel", () => {
  it("offers only the allowed steps and records a payment with its note", async () => {
    render(<OrderActionsPanel order={order({ canMarkPaid: true, canCancel: true })} texts={en.admin} errors={en.orders.errors} canManage />);
    expect(screen.queryByRole("button", { name: en.admin.orderShip })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: en.admin.orderMarkPaid }));
    fireEvent.change(screen.getByLabelText(en.admin.orderPaidNote), { target: { value: "Transfer 4711" } });
    fireEvent.click(screen.getByRole("button", { name: en.admin.orderConfirm }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call(0)).toEqual({ url: "/api/v1/admin/orders/1001/paid", body: { text: "Transfer 4711" } });
  });

  it("ships with the carrier and tracking code, and explains a refusal", async () => {
    fetchMock.mockImplementation(async () => new Response(JSON.stringify({ code: "order.status_invalid" }), { status: 409 }));
    render(<OrderActionsPanel order={order({ canShip: true })} texts={en.admin} errors={en.orders.errors} canManage />);

    fireEvent.click(screen.getByRole("button", { name: en.admin.orderShip }));
    fireEvent.change(screen.getByLabelText(en.admin.orderCarrier), { target: { value: "Post" } });
    fireEvent.change(screen.getByLabelText(en.admin.orderTrackingCode), { target: { value: "RR123" } });
    fireEvent.click(screen.getByRole("button", { name: en.admin.orderConfirm }));
    expect(await screen.findByText(en.orders.errors.status_invalid)).toBeTruthy();
    expect(call(0)).toEqual({ url: "/api/v1/admin/orders/1001/ship", body: { carrier: "Post", trackingCode: "RR123" } });
    expect(router.refresh).not.toHaveBeenCalled();
  });

  it("asks before canceling, sends the reason, and hides everything without orders.manage", async () => {
    const { rerender } = render(<OrderActionsPanel order={order({ canCancel: true })} texts={en.admin} errors={en.orders.errors} canManage />);
    fireEvent.click(screen.getByRole("button", { name: en.admin.orderCancel }));
    fireEvent.change(screen.getByLabelText(en.admin.orderCancelReason), { target: { value: "Out of stock" } });
    fireEvent.click(screen.getByRole("button", { name: en.admin.orderConfirm }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call(0)).toEqual({ url: "/api/v1/admin/orders/1001/cancel", body: { text: "Out of stock" } });

    rerender(<OrderActionsPanel order={order({ canCancel: true })} texts={en.admin} errors={en.orders.errors} canManage={false} />);
    expect(screen.queryByRole("button")).toBeNull();
  });
});
