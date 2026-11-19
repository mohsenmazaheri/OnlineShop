// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn(), replace: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { ProductTransfer, exportPath, importPath } from "./ProductTransfer";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const apiCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).startsWith("/api/v1/"));
const sheet = () => new File(["sku,price\nA,1\n"], "products.csv", { type: "text/csv" });

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("ProductTransfer", () => {
  it("builds the export and import addresses", () => {
    expect(exportPath("stock", "csv")).toBe("/api/v1/admin/catalog/export/stock?format=csv");
    expect(importPath("products", true)).toBe("/api/v1/admin/catalog/import/products?dryRun=true");
  });

  it("imports only after a clean check, then refreshes the shop", async () => {
    respond(200, { applied: false, created: 1, updated: 2, rows: 3, problems: [] });
    respond(200, { applied: true, created: 1, updated: 2, rows: 3, problems: [] });
    respond(200, {});
    render(<ProductTransfer locale="en" kind="products" texts={texts} />);

    const importButton = screen.getByRole("button", { name: texts.importApply }) as HTMLButtonElement;
    fireEvent.change(screen.getByLabelText(texts.importFile), { target: { files: [sheet()] } });
    expect(importButton.disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: texts.importCheck }));

    await waitFor(() => expect(screen.getByText(/No problems/)).toBeTruthy());
    expect(importButton.disabled).toBe(false);
    fireEvent.click(importButton);

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(apiCalls().map(([url]) => url)).toEqual(["/api/v1/admin/catalog/import/products?dryRun=true", "/api/v1/admin/catalog/import/products?dryRun=false"]);
    expect(screen.getByText(`${texts.importDone} 3 rows: 1 new, 2 updated.`)).toBeTruthy();
  });

  it("lists the problems with translated messages and keeps import off", async () => {
    respond(200, {
      applied: false,
      created: 0,
      updated: 0,
      rows: 2,
      problems: [
        { row: 2, column: "price", code: "import.number_invalid" },
        { row: 3, column: null, code: "catalog.gtin_invalid" },
      ],
    });
    render(<ProductTransfer locale="en" kind="stock" texts={texts} />);

    fireEvent.change(screen.getByLabelText(texts.importFile), { target: { files: [sheet()] } });
    fireEvent.click(screen.getByRole("button", { name: texts.importCheck }));

    await waitFor(() => expect(screen.getByText(texts.importErrors.number_invalid)).toBeTruthy());
    expect(screen.getByText(texts.catalogErrors.gtin_invalid)).toBeTruthy();
    expect((screen.getByRole("button", { name: texts.importApply }) as HTMLButtonElement).disabled).toBe(true);
    expect(apiCalls()[0][0]).toBe("/api/v1/admin/catalog/import/stock?dryRun=true");
  });

  it("shows why a whole file was refused", async () => {
    respond(400, { status: 400, code: "import.too_many_rows" });
    render(<ProductTransfer locale="en" kind="products" texts={texts} />);

    fireEvent.change(screen.getByLabelText(texts.importFile), { target: { files: [sheet()] } });
    fireEvent.click(screen.getByRole("button", { name: texts.importCheck }));

    await waitFor(() => expect(screen.getByText(texts.importErrors.too_many_rows)).toBeTruthy());
  });
});
