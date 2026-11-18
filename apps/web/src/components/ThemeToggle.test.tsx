// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ThemeToggle } from "./ThemeToggle";

const texts = { theme: "Theme", themeSystem: "System", themeLight: "Light", themeDark: "Dark" };
let systemDark = false;
const listeners = new Set<() => void>();

beforeEach(() => {
  systemDark = false;
  vi.stubGlobal("matchMedia", () => ({
    get matches() {
      return systemDark;
    },
    addEventListener: (_: string, fn: () => void) => listeners.add(fn),
    removeEventListener: (_: string, fn: () => void) => listeners.delete(fn),
  }));
});

afterEach(() => {
  cleanup();
  localStorage.clear();
  listeners.clear();
  delete document.documentElement.dataset.theme;
  vi.unstubAllGlobals();
});

describe("ThemeToggle", () => {
  it("cycles system → light → dark → system and remembers the choice", () => {
    render(<ThemeToggle texts={texts} />);
    const button = screen.getByRole("button", { name: "Theme: System" });

    fireEvent.click(button);
    expect(document.documentElement.dataset.theme).toBe("light");
    expect(localStorage.getItem("theme")).toBe("light");

    fireEvent.click(screen.getByRole("button", { name: "Theme: Light" }));
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(localStorage.getItem("theme")).toBe("dark");

    fireEvent.click(screen.getByRole("button", { name: "Theme: Dark" }));
    expect(localStorage.getItem("theme")).toBeNull();
    expect(screen.getByRole("button", { name: "Theme: System" })).toBeTruthy();
  });

  it("starts from the saved choice", () => {
    localStorage.setItem("theme", "dark");
    render(<ThemeToggle texts={texts} />);

    expect(screen.getByRole("button", { name: "Theme: Dark" })).toBeTruthy();
    expect(document.documentElement.dataset.theme).toBe("dark");
  });

  it("follows the operating system while on system", () => {
    render(<ThemeToggle texts={texts} />);
    expect(document.documentElement.dataset.theme).toBe("light");

    systemDark = true;
    act(() => listeners.forEach((fn) => fn()));

    expect(document.documentElement.dataset.theme).toBe("dark");
  });
});

describe("ThemeToggle across tabs", () => {
  it("picks up a choice made in another tab", () => {
    render(<ThemeToggle texts={texts} />);

    localStorage.setItem("theme", "dark");
    act(() => {
      window.dispatchEvent(new StorageEvent("storage", { key: "theme" }));
    });

    expect(screen.getByRole("button", { name: "Theme: Dark" })).toBeTruthy();
    expect(document.documentElement.dataset.theme).toBe("dark");
  });
});
