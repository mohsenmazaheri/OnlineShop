// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { ReservationsList } from "./ReservationsList";

const texts = en.admin;
const fetchMock = vi.fn();
const reservation = {
  id: "r1",
  productNumber: 1001,
  name: "Hoodie",
  variantLabel: "Beige / S",
  sku: "HOOD-S",
  quantity: 2,
  reference: "order:1042",
  createdUtc: "2026-11-20T10:00:00Z",
  expiresUtc: "2026-11-20T10:15:00Z",
};

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("ReservationsList", () => {
  it("releases a checkout's reservations after confirming", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValueOnce(false).mockReturnValueOnce(true);
    fetchMock.mockResolvedValueOnce(new Response("1", { status: 200 }));
    render(<ReservationsList locale="en" texts={texts} reservations={[reservation]} canManage />);

    const row = screen.getAllByRole("row")[1];
    expect(within(row).getByText("Beige / S")).toBeTruthy();
    expect(within(row).getByText("order:1042")).toBeTruthy();
    fireEvent.click(within(row).getByRole("button", { name: texts.reservationRelease }));
    expect(fetchMock).not.toHaveBeenCalled();
    fireEvent.click(within(row).getByRole("button", { name: texts.reservationRelease }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(confirm).toHaveBeenLastCalledWith(texts.reservationConfirmRelease.replace("{reference}", "order:1042"));
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/v1/admin/inventory/reservations/release");
    expect(JSON.parse(String(init.body))).toEqual({ reference: "order:1042" });
  });

  it("is read-only without inventory.manage and says so when nothing is reserved", () => {
    const { unmount } = render(<ReservationsList locale="en" texts={texts} reservations={[reservation]} canManage={false} />);
    expect(screen.queryByRole("button")).toBeNull();
    unmount();

    render(<ReservationsList locale="en" texts={texts} reservations={[]} canManage />);
    expect(screen.getByText(texts.reservationsNone)).toBeTruthy();
  });
});
