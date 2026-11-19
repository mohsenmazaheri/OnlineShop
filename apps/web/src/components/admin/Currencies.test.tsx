// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn(), replace: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { AddCurrencyForm, CurrencyForm, LocaleDefaultsForm, SyncRatesPanel, currencyErrorTexts, readCurrencyForm, type CurrencyView } from "./Currencies";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const sent = (i = 0) => ({ url: fetchMock.mock.calls[i][0] as string, init: fetchMock.mock.calls[i][1] as RequestInit });

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const usd: CurrencyView = {
  code: "USD",
  symbol: null,
  decimals: 2,
  roundingStep: 0.01,
  rate: 0.0000005,
  autoUpdate: true,
  rateUpdatedUtc: "2026-11-19T10:00:00Z",
  isEnabled: true,
  sortOrder: 1,
  isBase: false,
};
const irr: CurrencyView = { ...usd, code: "IRR", rate: 1, autoUpdate: false, isBase: true, decimals: 0, roundingStep: 1000, sortOrder: 0 };

describe("readCurrencyForm", () => {
  it("parses numbers (also with a decimal comma) and checkboxes", () => {
    const form = new FormData();
    for (const [k, v] of Object.entries({ symbol: " تومان ", decimals: "0", roundingStep: "1000", rate: "0,1", sortOrder: "3", isEnabled: "on" })) form.set(k, v);

    expect(readCurrencyForm(form, "IRT")).toEqual({
      code: "IRT",
      symbol: "تومان",
      decimals: 0,
      roundingStep: 1000,
      rate: 0.1,
      autoUpdate: false,
      isEnabled: true,
      sortOrder: 3,
    });
  });
});

describe("CurrencyForm", () => {
  it("saves the currency with PUT and refreshes", async () => {
    respond(200, usd);
    render(<CurrencyForm locale="en" texts={texts} base="IRR" currency={usd} />);

    fireEvent.click(screen.getByRole("button", { name: texts.save }));

    await waitFor(() => expect(screen.getByText(texts.saved)).toBeTruthy());
    expect(sent().url).toBe("/api/v1/admin/currencies/USD");
    expect(sent().init.method).toBe("PUT");
    expect(JSON.parse(String(sent().init.body))).toMatchObject({ code: "USD", rate: 0.0000005, autoUpdate: true, isEnabled: true });
    expect(router.refresh).toHaveBeenCalled();
  });

  it("keeps the base currency fixed at rate 1 and shows translated errors", async () => {
    respond(400, { status: 400, code: "currency.base_locked" });
    render(<CurrencyForm locale="en" texts={texts} base="IRR" currency={irr} />);

    expect((screen.getByLabelText(texts.currencyRate.replace("{base}", "IRR")) as HTMLInputElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: texts.save }));

    await waitFor(() => expect(screen.getByText(texts.currencyErrors.base_locked)).toBeTruthy());
    expect(JSON.parse(String(sent().init.body))).toMatchObject({ rate: 1, autoUpdate: false, isEnabled: true });
  });
});

describe("AddCurrencyForm", () => {
  it("posts an upper-cased code", async () => {
    respond(200, { ...usd, code: "GBP" });
    render(<AddCurrencyForm locale="en" texts={texts} base="IRR" />);

    fireEvent.change(screen.getByLabelText(texts.currencyCode), { target: { value: "gbp" } });
    fireEvent.click(screen.getByRole("button", { name: texts.addCurrency }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(sent().url).toBe("/api/v1/admin/currencies");
    expect(JSON.parse(String(sent().init.body)).code).toBe("GBP");
  });
});

describe("LocaleDefaultsForm and SyncRatesPanel", () => {
  it("saves a language's currency when the select changes", async () => {
    respond(200, { locale: "en", currencyCode: "IRR" });
    render(<LocaleDefaultsForm locale="en" texts={texts} defaults={[{ locale: "en", currencyCode: "USD" }]} currencies={[irr, usd]} languageNames={{ en: "English" }} />);

    fireEvent.change(screen.getByLabelText("English"), { target: { value: "IRR" } });

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(sent().url).toBe("/api/v1/admin/currencies/locales/en");
    expect(JSON.parse(String(sent().init.body))).toEqual({ currencyCode: "IRR" });
  });

  it("updates rates on demand and explains when automatic rates are off", async () => {
    respond(200, { updated: 4, syncedUtc: "2026-11-19T10:00:00Z" });
    const { rerender } = render(<SyncRatesPanel locale="en" texts={texts} source="CurrencyApi" />);

    fireEvent.click(screen.getByRole("button", { name: texts.syncNow }));
    await waitFor(() => expect(screen.getByText(texts.synced.replace("{count}", "4"))).toBeTruthy());

    rerender(<SyncRatesPanel locale="en" texts={texts} source="None" />);
    expect(screen.getByText(texts.syncOff)).toBeTruthy();
    expect(screen.queryByRole("button", { name: texts.syncNow })).toBeNull();
  });

  it("knows every currency error code", () => {
    const errors = currencyErrorTexts(texts);
    expect(errors.exists).toBe(texts.currencyErrors.exists);
    expect(errors.generic).toBe(texts.errors.generic);
  });
});
