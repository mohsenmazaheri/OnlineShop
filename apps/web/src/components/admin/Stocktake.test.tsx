// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn(), push: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { OpenStocktakeForm } from "./OpenStocktakeForm";
import { StocktakeActions } from "./StocktakeActions";
import { StocktakeCountForm } from "./StocktakeCountForm";
import { StocktakeLines } from "./StocktakeLines";
import { StocktakeList } from "./StocktakeList";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const call = (i = 0) => fetchMock.mock.calls[i] as [string, RequestInit];
const bodyOf = (i = 0) => JSON.parse(String(call(i)[1].body));

const main = { id: "w-main", code: "main", name: "Main warehouse", address: null, isDefault: true, isActive: true, units: 40, items: 2, used: true };
const old = { ...main, id: "w-old", name: "Old store", isDefault: false, isActive: false };
const line = (over: Partial<{ sku: string; counted: number | null; difference: number | null; variantLabel: string | null }> = {}) => ({
  itemId: over.sku ?? "SHOE-1",
  productNumber: 1001,
  name: "Walking shoes",
  variantLabel: null,
  sku: "SHOE-1",
  snapshot: 33,
  expected: 33,
  counted: 30,
  difference: -3,
  countedUtc: "2026-11-20T09:00:00Z",
  ...over,
});
const view = (lines = [line(), line({ sku: "CAP-1", counted: null, difference: null })], total = 2) => ({
  number: 4,
  warehouseId: "w-main",
  warehouse: "Main warehouse",
  status: "Open" as const,
  note: null,
  createdUtc: "2026-11-20T08:00:00Z",
  closedUtc: null,
  openedBy: "Sara",
  closedBy: null,
  totals: { lines: 2, counted: 1, withDifference: 1, unitsOver: 0, unitsShort: 3 },
  lines,
  total,
  page: 1,
  pageSize: 50,
});

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

describe("StocktakeList", () => {
  it("lists stocktakes with status and progress, linking to each", () => {
    render(
      <StocktakeList
        locale="en"
        texts={texts}
        stocktakes={[{ number: 4, warehouseId: "w", warehouse: "Main warehouse", status: "Posted", note: "yearly", createdUtc: "2026-11-20T08:00:00Z", closedUtc: "2026-11-20T10:00:00Z", lines: 20, counted: 18 }]}
      />,
    );

    const link = screen.getByRole("link");
    expect(link.getAttribute("href")).toBe("/en/admin/stocktakes/4");
    expect(within(link).getByText("ST-4")).toBeTruthy();
    expect(within(link).getByText(texts.stocktakeStatuses.Posted)).toBeTruthy();
    expect(within(link).getByText("18 of 20 items counted")).toBeTruthy();
    expect(within(link).getByText("yearly")).toBeTruthy();
  });

  it("says so when there are none", () => {
    render(<StocktakeList locale="en" texts={texts} stocktakes={[]} />);
    expect(screen.getByText(texts.stocktakesNone)).toBeTruthy();
  });
});

describe("OpenStocktakeForm", () => {
  it("starts a count of an active warehouse and opens its page", async () => {
    respond(200, { number: 5 });
    render(<OpenStocktakeForm locale="en" texts={texts} warehouses={[main, old]} />);

    expect(screen.queryByRole("option", { name: "Old store" })).toBeNull();
    fireEvent.change(screen.getByLabelText(texts.stocktakeNote), { target: { value: " yearly " } });
    fireEvent.submit(screen.getByRole("button", { name: texts.stocktakeStart }).closest("form")!);

    await waitFor(() => expect(router.push).toHaveBeenCalledWith("/en/admin/stocktakes/5"));
    expect(bodyOf()).toEqual({ warehouseId: "w-main", note: "yearly" });
  });

  it("shows why a count cannot start", async () => {
    respond(409, { code: "inventory.stocktake_already_open" });
    render(<OpenStocktakeForm locale="en" texts={texts} warehouses={[main]} />);
    fireEvent.submit(screen.getByRole("button", { name: texts.stocktakeStart }).closest("form")!);
    expect((await screen.findByRole("alert")).textContent).toBe(texts.inventoryErrors.stocktake_already_open);
  });
});

describe("StocktakeCountForm", () => {
  it("records a count, shows the difference and clears the SKU for the next item", async () => {
    respond(200, [line({ counted: 30, difference: -3 })]);
    render(<StocktakeCountForm locale="en" texts={texts} number={4} />);

    fireEvent.change(screen.getByLabelText(texts.movementSku), { target: { value: " SHOE-1 " } });
    fireEvent.change(screen.getByLabelText(texts.movementQuantity), { target: { value: "30" } });
    fireEvent.submit(screen.getByRole("button", { name: texts.stocktakeCountSave }).closest("form")!);

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call()[0]).toBe("/api/v1/admin/inventory/stocktakes/4/counts?locale=en");
    expect(bodyOf()).toEqual({ counts: [{ sku: "SHOE-1", quantity: 30 }], add: false });
    expect(screen.getByRole("status").textContent).toBe("SHOE-1: counted 30, difference −3");
    expect((screen.getByLabelText(texts.movementSku) as HTMLInputElement).value).toBe("");
  });

  it("adds piece by piece in add mode", async () => {
    respond(200, [line({ counted: 31, difference: -2 })]);
    render(<StocktakeCountForm locale="en" texts={texts} number={4} />);

    fireEvent.click(screen.getByRole("checkbox", { name: texts.stocktakeAddMode }));
    expect((screen.getByLabelText(texts.movementQuantity) as HTMLInputElement).value).toBe("1");
    fireEvent.change(screen.getByLabelText(texts.movementSku), { target: { value: "SHOE-1" } });
    fireEvent.submit(screen.getByRole("button", { name: texts.stocktakeCountSave }).closest("form")!);

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(bodyOf()).toEqual({ counts: [{ sku: "SHOE-1", quantity: 1 }], add: true });
  });
});

describe("StocktakeLines", () => {
  it("shows each item's difference and uncounted items with a dash", () => {
    render(<StocktakeLines locale="en" texts={texts} stocktake={view()} filter="All" search="" />);

    const [shoes, cap] = screen.getAllByRole("row").slice(1);
    expect(within(shoes).getByText("−3")).toBeTruthy();
    expect(within(cap).getAllByText("—")).toHaveLength(2);
    expect(screen.getByRole("link", { name: texts.stocktakeFilters.All }).getAttribute("aria-current")).toBe("page");
    expect(screen.getByRole("link", { name: texts.stocktakeFilters.Differences }).getAttribute("href")).toBe("/en/admin/stocktakes/4?filter=Differences");
  });

  it("keeps the filter and search in page links", () => {
    render(<StocktakeLines locale="en" texts={texts} stocktake={view([], 120)} filter="Uncounted" search="cap" />);

    expect(screen.getByText(texts.stocktakeNoLines)).toBeTruthy();
    expect(screen.getByRole("link", { name: "3" }).getAttribute("href")).toBe("/en/admin/stocktakes/4?filter=Uncounted&search=cap&page=3");
  });
});

describe("StocktakeActions", () => {
  it("offers the sheet, imports counts and reports problems", async () => {
    respond(200, { applied: false, created: 0, updated: 0, rows: 2, problems: [{ row: 2, column: "sku", code: "import.sku_not_found" }] });
    respond(200, { applied: true, created: 0, updated: 2, rows: 2, problems: [] });
    render(<StocktakeActions locale="en" texts={texts} number={4} />);

    expect(screen.getByRole("link", { name: "CSV" }).getAttribute("href")).toBe("/api/v1/admin/inventory/stocktakes/4/sheet?format=csv&locale=en");
    const input = screen.getByLabelText(texts.stocktakeImport);
    fireEvent.change(input, { target: { files: [new File(["sku,counted"], "counts.csv", { type: "text/csv" })] } });
    fireEvent.click(screen.getByRole("button", { name: texts.stocktakeImportSend }));
    expect(await screen.findByText(new RegExp(texts.importErrors.sku_not_found))).toBeTruthy();
    expect(router.refresh).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: texts.stocktakeImportSend }));
    expect(await screen.findByText("2 counts recorded.")).toBeTruthy();
    expect(call(1)[0]).toBe("/api/v1/admin/inventory/stocktakes/4/counts/import");
    expect(router.refresh).toHaveBeenCalled();
  });

  it("posts only after confirming, and shows a refusal", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValueOnce(false).mockReturnValueOnce(true);
    respond(400, { code: "inventory.stocktake_nothing_counted" });
    render(<StocktakeActions locale="en" texts={texts} number={4} />);

    fireEvent.click(screen.getByRole("button", { name: texts.stocktakePost }));
    expect(fetchMock).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: texts.stocktakePost }));
    expect((await screen.findByRole("alert")).textContent).toBe(texts.inventoryErrors.stocktake_nothing_counted);
    expect(call()).toEqual(["/api/v1/admin/inventory/stocktakes/4/post", expect.objectContaining({ method: "POST" })]);
    expect(confirm).toHaveBeenCalledWith(texts.stocktakeConfirmPost);
  });

  it("cancels after confirming", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    respond(200, { number: 4, status: "Cancelled" });
    render(<StocktakeActions locale="en" texts={texts} number={4} />);

    fireEvent.click(screen.getByRole("button", { name: texts.stocktakeCancel }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call()[0]).toBe("/api/v1/admin/inventory/stocktakes/4/cancel");
  });
});
