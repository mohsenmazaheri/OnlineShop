// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { applyTheme, nextPreference, parsePreference, resolveTheme, themeColors, themeScript } from "./theme";

afterEach(() => {
  vi.unstubAllGlobals(); // first: one test replaces localStorage with a throwing stub
  localStorage.clear();
  delete document.documentElement.dataset.theme;
  document.documentElement.style.colorScheme = "";
  document.head.innerHTML = "";
});

const runScript = (systemDark: boolean) => {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: systemDark && query.includes("dark") }));
  new Function(themeScript)();
};

describe("theme preference", () => {
  it("accepts only the three known values", () => {
    expect(parsePreference("dark")).toBe("dark");
    expect(parsePreference("purple")).toBe("system");
    expect(parsePreference(null)).toBe("system");
  });

  it("follows the system only when asked to", () => {
    expect(resolveTheme("system", true)).toBe("dark");
    expect(resolveTheme("system", false)).toBe("light");
    expect(resolveTheme("light", true)).toBe("light");
    expect(resolveTheme("dark", false)).toBe("dark");
  });

  it("cycles system → light → dark → system", () => {
    expect([nextPreference("system"), nextPreference("light"), nextPreference("dark")]).toEqual(["light", "dark", "system"]);
  });
});

describe("head script", () => {
  it("applies the saved choice before paint", () => {
    localStorage.setItem("theme", "dark");
    runScript(false);

    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(document.documentElement.style.colorScheme).toBe("dark");
  });

  it("follows the system when nothing is saved", () => {
    runScript(true);
    expect(document.documentElement.dataset.theme).toBe("dark");

    localStorage.setItem("theme", "light");
    runScript(true);
    expect(document.documentElement.dataset.theme).toBe("light");
  });

  it("never throws when storage is blocked", () => {
    vi.stubGlobal("localStorage", {
      getItem: () => {
        throw new Error("blocked");
      },
    });

    expect(() => new Function(themeScript)()).not.toThrow();
  });
});

describe("applyTheme", () => {
  it("sets the attribute, color-scheme and the browser colour", () => {
    document.head.innerHTML = '<meta name="theme-color" content="#fff" media="(prefers-color-scheme: light)">';

    applyTheme("dark");

    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')!.content).toBe(themeColors.dark);
  });
});
