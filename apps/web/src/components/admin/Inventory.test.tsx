// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { MovementList } from "./MovementList";
import { StockList } from "./StockList";
import { StockMovementForm } from "./StockMovementForm";
import { WarehousesEditor } from "./WarehousesEditor";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const call = (i = 0) => fetchMock.mock.calls[i] as [string, RequestInit];
const bodyOf = (i = 0) => JSON.parse(String(call(i)[1].body));

const main = { id: "w-main", code: "main", name: "Main warehouse", address: null, isDefault: true, isActive: true, units: 40, items: 2, used: true };
const shop = { id: "w-shop", code: "shop", name: "Shop floor", address: "1 High St", isDefault: false, isActive: true, units: 3, items: 1, used: false };
const old = { id: "w-old", code: "old", name: "Old store", address: null, isDefault: false, isActive: false, units: 0, items: 0, used: true };
const row = (
  over: Partial<{
    sku: string;
    total: number;
    reserved: number;
    available: number;
    variantLabel: string | null;
    averageCost: number | null;
    minimumStock: number | null;
    low: boolean;
    value: number | null;
  }> = {},
) => ({
  itemId: over.sku ?? "SHOE-1",
  productNumber: 1001,
  name: "Walking shoes",
  variantLabel: null,
  sku: "SHOE-1",
  isPublished: true,
  total: 33,
  reserved: 0,
  available: 33,
  levels: [
    { warehouseId: "w-main", quantity: 30 },
    { warehouseId: "w-shop", quantity: 3 },
  ],
  averageCost: null as number | null,
  minimumStock: null as number | null,
  low: false,
  value: null as number | null,
  ...over,
});

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("StockList", () => {
  const page = {
    items: [row({ averageCost: 20, value: 660, minimumStock: 40, low: true, reserved: 5, available: 28 }), row({ sku: "HOOD-S", total: 0, available: 0, variantLabel: "Beige / S" })],
    total: 2,
    page: 1,
    pageSize: 20,
    warehouses: [main, shop, old],
    lowCount: 1,
    currency: "EUR",
  };

  it("shows a column per active warehouse, the total and links to the ledger and the movement form", () => {
    render(<StockList locale="en" texts={texts} page={page} search="" onlyOut={false} canManage />);

    const headers = screen.getAllByRole("columnheader").map((h) => h.textContent);
    expect(headers).toEqual([texts.stockItem, "Main warehouse", "Shop floor", texts.stockTotal, texts.stockAverageCost, texts.stockValue, texts.stockHistory]);
    const hoodie = screen.getByText("Beige / S").closest("tr")!;
    expect(within(hoodie).getByText("0", { selector: "td.text-danger" })).toBeTruthy();
    expect(within(hoodie).getByRole("link", { name: texts.stockHistory }).getAttribute("href")).toBe("/en/admin/inventory/movements?sku=HOOD-S");
    expect(within(hoodie).getByRole("link", { name: texts.stockMove }).getAttribute("href")).toBe("/en/admin/inventory?sku=HOOD-S#move");
  });

  it("offers no movement links without inventory.manage and keeps the filters in page links", () => {
    render(<StockList locale="en" texts={texts} page={{ ...page, total: 45 }} search="shoe" onlyOut canManage={false} />);

    expect(screen.queryByRole("link", { name: texts.stockMove })).toBeNull();
    expect(screen.getByRole("link", { name: "3" }).getAttribute("href")).toBe("/en/admin/inventory?search=shoe&onlyOut=true&page=3");
    expect((screen.getByRole("checkbox", { name: texts.stockOnlyOut }) as HTMLInputElement).checked).toBe(true);
  });

  it("shows cost, value, the minimum and a banner for items running low", () => {
    render(<StockList locale="en" texts={texts} page={page} search="" onlyOut={false} canManage />);

    expect(screen.getByRole("status").textContent).toContain("1 items are at or below their minimum stock.");
    expect(screen.getByRole("link", { name: texts.stockLowShow }).getAttribute("href")).toBe("/en/admin/inventory?onlyLow=true");
    const shoes = screen.getByText("SHOE-1", { exact: false }).closest("tr")!;
    expect(within(shoes).getByText(texts.stockLow)).toBeTruthy();
    expect(within(shoes).getByText("€20")).toBeTruthy();
    expect(within(shoes).getByText("€660")).toBeTruthy();
    expect(within(shoes).getByText(`${texts.stockMinimum} 40`)).toBeTruthy();
    expect(within(shoes).getByText("5 reserved · 28 available")).toBeTruthy();
    expect(within(screen.getByText("Beige / S").closest("tr")!).getAllByText("—")).toHaveLength(2);
  });

  it("hides the banner while showing only the low items", () => {
    render(<StockList locale="en" texts={texts} page={page} search="" onlyOut={false} onlyLow canManage />);
    expect(screen.queryByRole("status")).toBeNull();
    expect((screen.getByRole("checkbox", { name: texts.stockOnlyLow }) as HTMLInputElement).checked).toBe(true);
  });
});

describe("StockMovementForm", () => {
  it("posts a receipt to the chosen warehouse and shows the new stock", async () => {
    respond(200, row({ total: 40 }));
    render(<StockMovementForm locale="en" texts={texts} warehouses={[main, shop, old]} sku="SHOE-1" />);

    expect(screen.queryByRole("option", { name: "Old store" })).toBeNull();
    fireEvent.change(screen.getByLabelText(texts.movementQuantity), { target: { value: "7" } });
    fireEvent.change(screen.getByLabelText(texts.movementReference), { target: { value: "PO-9" } });
    fireEvent.submit(screen.getByRole("button", { name: texts.movementSave }).closest("form")!);

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call()[0]).toBe("/api/v1/admin/inventory/movements?locale=en");
    expect(bodyOf()).toEqual({ sku: "SHOE-1", quantity: 7, reference: "PO-9", note: null, kind: "Receipt", warehouseId: "w-main" });
    expect(screen.getByRole("status").textContent).toBe("Saved. SHOE-1 now has 40 in stock.");
  });

  it("transfers between two warehouses and shows the server's reason when it refuses", async () => {
    respond(409, { code: "inventory.insufficient" });
    render(<StockMovementForm locale="en" texts={texts} warehouses={[main, shop]} sku="" />);

    fireEvent.change(screen.getByLabelText(texts.movementType), { target: { value: "Transfer" } });
    fireEvent.change(screen.getByLabelText(texts.movementSku), { target: { value: " SHOE-1 " } });
    fireEvent.change(screen.getByLabelText(texts.movementQuantity), { target: { value: "50" } });
    fireEvent.submit(screen.getByRole("button", { name: texts.movementSave }).closest("form")!);

    expect(await screen.findByRole("alert")).toHaveProperty("textContent", texts.inventoryErrors.insufficient);
    expect(call()[0]).toBe("/api/v1/admin/inventory/transfers?locale=en");
    expect(bodyOf()).toMatchObject({ sku: "SHOE-1", quantity: 50, fromWarehouseId: "w-main", toWarehouseId: "w-shop" });
    expect(router.refresh).not.toHaveBeenCalled();
  });

  it("offers a transfer only with two active warehouses and a negative number only for adjustments", () => {
    render(<StockMovementForm locale="en" texts={texts} warehouses={[main, old]} sku="" />);

    expect(screen.queryByRole("option", { name: texts.movementTransfer })).toBeNull();
    expect(screen.getByLabelText(texts.movementQuantity).getAttribute("min")).toBe("1");
    fireEvent.change(screen.getByLabelText(texts.movementType), { target: { value: "Adjustment" } });
    expect(screen.getByLabelText(texts.movementQuantity).getAttribute("min")).toBeNull();
    expect(screen.getByText(texts.movementAdjustHint)).toBeTruthy();
  });
});

describe("MovementList", () => {
  const line = {
    id: "m1",
    createdUtc: "2026-11-19T10:00:00Z",
    warehouseId: "w-main",
    warehouse: "Main warehouse",
    productNumber: 1001,
    name: "Walking shoes",
    variantLabel: null,
    sku: "SHOE-1",
    kind: "Adjustment" as const,
    quantity: -2,
    balanceAfter: 31,
    reference: "auto:editor",
    note: null,
    transferId: null,
    userName: null,
  };

  it("shows signed quantities, translated kinds and references, and automatic lines without a user", () => {
    render(
      <MovementList
        locale="en"
        texts={texts}
        page={{ items: [line, { ...line, id: "m2", kind: "Receipt", quantity: 5, reference: "PO-1", note: "late", userName: "Sara" }], total: 2, page: 1, pageSize: 50 }}
        warehouses={[main, shop]}
        filters={{ sku: "SHOE-1", warehouseId: "", kind: "" }}
      />,
    );

    const [adjustment, receipt] = screen.getAllByRole("row").slice(1);
    expect(within(adjustment).getByText("−2")).toBeTruthy();
    expect(within(adjustment).getByText(texts.stockReferences.editor)).toBeTruthy();
    expect(within(adjustment).getByText(texts.movementsSystem)).toBeTruthy();
    expect(within(receipt).getByText(texts.movementKinds.Receipt)).toBeTruthy();
    expect(within(receipt).getByText("+5")).toBeTruthy();
    expect(within(receipt).getByText("PO-1")).toBeTruthy();
    expect(within(receipt).getByText("Sara")).toBeTruthy();
    expect((screen.getByRole("searchbox") as HTMLInputElement).value).toBe("SHOE-1");
  });

  it("says so when nothing matches", () => {
    render(<MovementList locale="en" texts={texts} page={{ items: [], total: 0, page: 1, pageSize: 50 }} warehouses={[]} filters={{ sku: "", warehouseId: "", kind: "" }} />);
    expect(screen.getByText(texts.movementsNone)).toBeTruthy();
  });
});

describe("WarehousesEditor", () => {
  it("adds a warehouse and keeps the default one active and default", async () => {
    respond(200, shop);
    respond(200, main);
    render(<WarehousesEditor locale="en" texts={texts} warehouses={[main, shop]} canManage />);

    const add = screen.getByText(`+ ${texts.warehouseNew}`).closest("details")!;
    fireEvent.change(within(add).getByLabelText(texts.warehouseName), { target: { value: " Branch " } });
    fireEvent.submit(within(add).getByRole("button", { name: texts.add }).closest("form")!);
    await waitFor(() => expect(router.refresh).toHaveBeenCalledTimes(1));
    expect(call()[0]).toBe("/api/v1/admin/inventory/warehouses");
    expect(bodyOf()).toEqual({ code: null, name: "Branch", address: null, isActive: true, isDefault: false });

    const first = screen.getByText("Main warehouse").closest("details")!;
    expect(within(first).queryByRole("button", { name: texts.remove })).toBeNull();
    fireEvent.submit(within(first).getByRole("button", { name: texts.save }).closest("form")!);
    await waitFor(() => expect(router.refresh).toHaveBeenCalledTimes(2));
    expect(call(1)).toEqual(["/api/v1/admin/inventory/warehouses/w-main", expect.objectContaining({ method: "PUT" })]);
    expect(bodyOf(1)).toMatchObject({ isActive: true, isDefault: true });
  });

  it("deletes an unused warehouse after asking, and is read-only without inventory.manage", async () => {
    respond(204);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const { unmount } = render(<WarehousesEditor locale="en" texts={texts} warehouses={[main, shop]} canManage />);

    fireEvent.click(within(screen.getByText("Shop floor").closest("details")!).getByRole("button", { name: texts.remove }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call()).toEqual(["/api/v1/admin/inventory/warehouses/w-shop", expect.objectContaining({ method: "DELETE" })]);
    unmount();

    render(<WarehousesEditor locale="en" texts={texts} warehouses={[main, old]} canManage={false} />);
    expect(screen.getByText(texts.warehouseReadOnly)).toBeTruthy();
    expect(screen.queryByRole("button")).toBeNull();
    expect(screen.getByText(texts.warehouseInactive)).toBeTruthy();
  });
});
