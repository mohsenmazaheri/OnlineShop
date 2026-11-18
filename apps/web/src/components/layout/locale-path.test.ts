import { describe, expect, it } from "vitest";
import { activeTab, switchLocalePath } from "./locale-path";

const locales = ["fa", "en", "de"];

describe("switchLocalePath", () => {
  it("keeps the page and swaps the language", () => {
    expect(switchLocalePath("/fa/account/addresses", "en", locales)).toBe("/en/account/addresses");
    expect(switchLocalePath("/fa", "de", locales)).toBe("/de");
    expect(switchLocalePath("/fa/", "de", locales)).toBe("/de");
  });

  it("goes to the home page of that language when the path has no known locale", () => {
    expect(switchLocalePath("/xx/page", "en", locales)).toBe("/en");
    expect(switchLocalePath(null, "en", locales)).toBe("/en");
  });
});

describe("activeTab", () => {
  it.each([
    ["/fa", "home"],
    ["/fa/", "home"],
    ["/fa/cart", "cart"],
    ["/fa/account", "account"],
    ["/fa/account/addresses", "account"],
    ["/fa/accounting", null],
    ["/fa/search", null],
    [null, null],
  ] as const)("%s → %s", (path, tab) => {
    expect(activeTab(path, "fa")).toBe(tab);
  });
});
