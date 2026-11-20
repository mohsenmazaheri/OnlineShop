// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn(), push: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));
vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));

import { ItemSettingsForm } from "./ItemSettingsForm";
import { PurchaseList, ReceiptLines, receiptTotals } from "./PurchaseList";
import { ReceiptEditor, toLines } from "./ReceiptEditor";
import { SuppliersEditor } from "./SuppliersEditor";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const call = (i = 0) => fetchMock.mock.calls[i] as [string, RequestInit];
const bodyOf = (i = 0) => JSON.parse(String(call(i)[1].body));

const rana = { id: "s-1", name: "Rana Trading", contactName: "Sara", phone: "0912", email: null, address: null, note: null, isActive: true, receipts: 2 };
const idle = { ...rana, id: "s-2", name: "Idle Co", isActive: false, receipts: 0 };
const main = { id: "w-main", code: "main", name: "Main warehouse", address: null, isDefault: true, isActive: true, units: 40, items: 2, used: true };
const summary = {
  number: 3,
  supplierId: "s-1",
  supplier: "Rana Trading",
  warehouseId: "w-main",
  warehouse: "Main warehouse",
  status: "Draft" as const,
  supplierReference: "INV-9",
  currency: "EUR",
  lines: 1,
  units: 4,
  total: 10,
  createdUtc: "2026-11-20T08:00:00Z",
  closedUtc: null,
};
const receipt = {
  summary,
  note: null,
  createdBy: "Sara",
  closedBy: null,
  lines: [{ itemId: "i1", productNumber: 1001, name: "Walking shoes", variantLabel: null, sku: "SHOE-1", quantity: 4, unitCost: 2.5, total: 10 }],
};

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  router.push.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("SuppliersEditor", () => {
  it("adds a supplier with trimmed, optional fields", async () => {
    respond(200, rana);
    render(<SuppliersEditor locale="en" texts={texts} suppliers={[rana]} canManage />);

    const add = screen.getByText(`+ ${texts.supplierNew}`).closest("details")!;
    fireEvent.change(within(add).getByLabelText(texts.supplierName), { target: { value: " Nova " } });
    fireEvent.change(within(add).getByLabelText(texts.supplierEmail), { target: { value: "sales@nova.test" } });
    fireEvent.submit(within(add).getByRole("button", { name: texts.add }).closest("form")!);

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(bodyOf()).toEqual({ name: "Nova", contactName: null, phone: null, email: "sales@nova.test", address: null, note: null, isActive: true });
  });

  it("deletes only suppliers without receipts and is read-only without inventory.manage", () => {
    const { unmount } = render(<SuppliersEditor locale="en" texts={texts} suppliers={[rana, idle]} canManage />);
    expect(within(screen.getByText("Rana Trading").closest("details")!).queryByRole("button", { name: texts.remove })).toBeNull();
    expect(within(screen.getByText("Idle Co").closest("details")!).getByRole("button", { name: texts.remove })).toBeTruthy();
    expect(screen.getByText("2 receipts")).toBeTruthy();
    unmount();

    render(<SuppliersEditor locale="en" texts={texts} suppliers={[]} canManage={false} />);
    expect(screen.getByText(texts.suppliersNone)).toBeTruthy();
    expect(screen.queryByText(`+ ${texts.supplierNew}`)).toBeNull();
  });
});

describe("ReceiptEditor", () => {
  it("turns the rows into API lines, reading Persian digits and decimal commas", () => {
    expect(
      toLines([
        { key: 0, sku: " SHOE-1 ", quantity: "۴", unitCost: "2,5" },
        { key: 1, sku: " ", quantity: "1", unitCost: "1" },
        { key: 2, sku: "CAP", quantity: "", unitCost: "" },
      ]),
    ).toEqual([
      { sku: "SHOE-1", quantity: 4, unitCost: 2.5 },
      { sku: "CAP", quantity: 0, unitCost: -1 },
    ]);
  });

  it("writes a new draft and opens it", async () => {
    respond(200, receipt);
    render(<ReceiptEditor locale="en" texts={texts} currency="EUR" suppliers={[rana, idle]} warehouses={[main]} />);

    expect(screen.queryByRole("option", { name: "Idle Co" })).toBeNull();
    fireEvent.change(screen.getByLabelText(`${texts.movementSku} 1`), { target: { value: "SHOE-1" } });
    fireEvent.change(screen.getByLabelText(`${texts.movementQuantity} 1`), { target: { value: "4" } });
    fireEvent.change(screen.getByLabelText(`${texts.purchaseUnitCost.replace("{currency}", "EUR")} 1`), { target: { value: "2.5" } });
    fireEvent.click(screen.getByRole("button", { name: texts.purchaseAddLine }));
    expect(screen.getByLabelText(`${texts.movementSku} 2`)).toBeTruthy();
    fireEvent.submit(screen.getByRole("button", { name: texts.purchaseSave }).closest("form")!);

    await waitFor(() => expect(router.push).toHaveBeenCalledWith("/en/admin/purchases/3"));
    expect(call()[0]).toBe("/api/v1/admin/inventory/purchases?locale=en");
    expect(bodyOf()).toEqual({ supplierId: "s-1", warehouseId: "w-main", supplierReference: null, note: null, lines: [{ sku: "SHOE-1", quantity: 4, unitCost: 2.5 }] });
  });

  it("saves a draft before posting it, and only after confirming", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValueOnce(false).mockReturnValueOnce(true);
    respond(200, receipt);
    respond(200, { ...receipt, summary: { ...summary, status: "Posted" } });
    render(<ReceiptEditor locale="en" texts={texts} currency="EUR" suppliers={[rana]} warehouses={[main]} receipt={receipt} />);

    expect((screen.getByLabelText(`${texts.movementSku} 1`) as HTMLInputElement).value).toBe("SHOE-1");
    fireEvent.click(screen.getByRole("button", { name: texts.purchasePost }));
    expect(fetchMock).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: texts.purchasePost }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call(0)).toEqual(["/api/v1/admin/inventory/purchases/3?locale=en", expect.objectContaining({ method: "PUT" })]);
    expect(call(1)[0]).toBe("/api/v1/admin/inventory/purchases/3/post?locale=en");
    expect(confirm).toHaveBeenCalledWith(texts.purchaseConfirmPost);
  });

  it("shows the server's reason and asks for a supplier when there is none", async () => {
    respond(400, { code: "inventory.item_repeated" });
    const { unmount } = render(<ReceiptEditor locale="en" texts={texts} currency="EUR" suppliers={[rana]} warehouses={[main]} receipt={receipt} />);
    fireEvent.submit(screen.getByRole("button", { name: texts.purchaseSave }).closest("form")!);
    expect((await screen.findByRole("alert")).textContent).toBe(texts.inventoryErrors.item_repeated);
    unmount();

    render(<ReceiptEditor locale="en" texts={texts} currency="EUR" suppliers={[idle]} warehouses={[main]} />);
    expect(screen.getByText(texts.purchaseNoSuppliers)).toBeTruthy();
  });
});

describe("PurchaseList", () => {
  it("lists receipts with status and server totals", () => {
    render(<PurchaseList locale="en" texts={texts} receipts={[summary]} />);

    const link = screen.getByRole("link");
    expect(link.getAttribute("href")).toBe("/en/admin/purchases/3");
    expect(within(link).getByText("PR-3")).toBeTruthy();
    expect(within(link).getByText(texts.purchaseStatuses.Draft)).toBeTruthy();
    expect(receiptTotals(summary, texts, "en")).toBe("1 items · 4 units · total €10");
  });

  it("shows a closed receipt's lines read-only", () => {
    render(<ReceiptLines locale="en" texts={texts} receipt={receipt} />);
    const row = screen.getAllByRole("row")[1];
    expect(within(row).getByText("€2.50")).toBeTruthy();
    expect(within(row).getByText("€10")).toBeTruthy();
  });

  it("says so when there are none", () => {
    render(<PurchaseList locale="en" texts={texts} receipts={[]} />);
    expect(screen.getByText(texts.purchasesNone)).toBeTruthy();
  });
});

describe("ItemSettingsForm", () => {
  it("saves a minimum and leaves the cost alone when it is empty", async () => {
    respond(200, { itemId: "i1", sku: "SHOE-1", minimumStock: 5, averageCost: null });
    render(<ItemSettingsForm texts={texts} currency="EUR" sku="SHOE-1" />);

    fireEvent.change(screen.getByLabelText(texts.itemSettingsMinimum), { target: { value: "۵" } });
    fireEvent.submit(screen.getByRole("button", { name: texts.itemSettingsSave }).closest("form")!);

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call()).toEqual(["/api/v1/admin/inventory/items/settings", expect.objectContaining({ method: "PUT" })]);
    expect(bodyOf()).toEqual({ sku: "SHOE-1", minimumStock: 5, averageCost: null });
    expect(screen.getByRole("status").textContent).toBe(texts.itemSettingsSaved);
  });
});
