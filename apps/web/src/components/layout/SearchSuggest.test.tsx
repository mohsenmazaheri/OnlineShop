// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));

import { debounceMs, SearchSuggest } from "./SearchSuggest";

const money = { code: "EUR", symbol: null, decimals: 2, roundingStep: 0.01, rate: 1, isBase: true, base: "EUR" };
const suggestions = {
  products: [{ number: 1018, name: "Ranna Hoodie", slug: "ranna-hoodie", imageUrl: null, price: 49.5, compareAtPrice: null, inStock: true }],
  categories: [{ name: "Home & Kitchen", path: "c/home-kitchen" }],
  brands: [],
  terms: ["hoodie zip"],
};
const fetchMock = vi.fn();

beforeEach(() => {
  vi.useFakeTimers();
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(new Response(JSON.stringify(suggestions), { status: 200 }));
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

async function type(text: string) {
  fireEvent.change(screen.getByRole("combobox"), { target: { value: text } });
  await act(async () => {
    await vi.advanceTimersByTimeAsync(debounceMs);
  });
}

describe("SearchSuggest", () => {
  it("asks once after typing stops and shows products with price, places and popular searches", async () => {
    render(<SearchSuggest locale="en" texts={en.header} money={money} />);

    fireEvent.change(screen.getByRole("combobox"), { target: { value: "h" } });
    await type("ho");
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/catalog/suggest?q=ho&locale=en");

    const list = screen.getByRole("listbox", { name: en.header.suggestLabel });
    expect(within(list).getByRole("option", { name: /Ranna Hoodie/ }).getAttribute("href")).toBe("/en/p/1018/ranna-hoodie");
    expect(within(list).getByText("€49.50")).toBeTruthy();
    expect(within(list).getByRole("option", { name: "Home & Kitchen" }).getAttribute("href")).toBe("/en/c/home-kitchen");
    expect(within(list).getByRole("option", { name: /hoodie zip/ }).getAttribute("href")).toBe("/en/search?q=hoodie%20zip");
    expect(within(list).getByRole("option", { name: en.header.suggestAll.replace("{query}", "ho") })).toBeTruthy();
    expect(screen.getByRole("combobox").getAttribute("aria-expanded")).toBe("true");
  });

  it("moves through the suggestions with the arrow keys and closes on Escape", async () => {
    render(<SearchSuggest locale="en" texts={en.header} money={money} />);
    await type("hoo");
    const input = screen.getByRole("combobox");

    fireEvent.keyDown(input, { key: "ArrowDown" });
    const first = screen.getAllByRole("option")[0];
    expect(input.getAttribute("aria-activedescendant")).toBe(first.id);
    expect(first.getAttribute("aria-selected")).toBe("true");
    // Up from the first wraps round to the last ("all results").
    fireEvent.keyDown(input, { key: "ArrowUp" });
    expect(input.getAttribute("aria-activedescendant")).toBe(screen.getAllByRole("option").at(-1)!.id);

    fireEvent.keyDown(input, { key: "Escape" });
    expect(screen.queryByRole("listbox")).toBeNull();
  });

  it("asks nothing for one letter and stays a plain GET form", async () => {
    render(<SearchSuggest locale="fa" texts={en.header} money={money} defaultValue="x" />);
    await type("x");
    expect(fetchMock).not.toHaveBeenCalled();
    const form = screen.getByRole("search");
    expect(form.getAttribute("action")).toBe("/fa/search");
    expect(form.getAttribute("method")).toBe("get");
    expect((screen.getByRole("combobox") as HTMLInputElement).name).toBe("q");
  });
});
