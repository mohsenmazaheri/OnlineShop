// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import { InstallPrompt, dismissKey, isIosSafari } from "./InstallPrompt";

const texts = en.footer;

function standalone(matches: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: query.includes("standalone") && matches, addEventListener() {}, removeEventListener() {} }));
}

beforeEach(() => {
  localStorage.clear();
  standalone(false);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function fireInstallEvent() {
  const prompt = vi.fn(async () => undefined);
  const event = Object.assign(new Event("beforeinstallprompt", { cancelable: true }), {
    prompt,
    userChoice: Promise.resolve({ outcome: "accepted" as const }),
  });
  act(() => {
    window.dispatchEvent(event);
  });
  return prompt;
}

describe("InstallPrompt", () => {
  it("shows nothing until the browser offers installation, then installs on click", async () => {
    render(<InstallPrompt texts={texts} />);
    expect(screen.queryByRole("region")).toBeNull();

    const prompt = fireInstallEvent();
    fireEvent.click(screen.getByRole("button", { name: texts.installButton }));

    expect(prompt).toHaveBeenCalled();
    await act(async () => {});
    expect(screen.queryByRole("region")).toBeNull();
  });

  it("remembers that the visitor closed it", () => {
    render(<InstallPrompt texts={texts} />);
    fireInstallEvent();

    fireEvent.click(screen.getByRole("button", { name: texts.installDismiss }));

    expect(screen.queryByRole("region")).toBeNull();
    expect(localStorage.getItem(dismissKey)).toBe("1");
  });

  it("stays hidden when installed or dismissed earlier", () => {
    standalone(true);
    const { unmount } = render(<InstallPrompt texts={texts} />);
    fireInstallEvent();
    expect(screen.queryByRole("region")).toBeNull();
    unmount();

    standalone(false);
    localStorage.setItem(dismissKey, "1");
    render(<InstallPrompt texts={texts} />);
    fireInstallEvent();
    expect(screen.queryByRole("region")).toBeNull();
  });
});

describe("isIosSafari", () => {
  it("recognises Safari on iPhone and not other iOS browsers", () => {
    expect(isIosSafari("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1")).toBe(true);
    expect(isIosSafari("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) CriOS/130.0 Mobile/15E148 Safari/604.1")).toBe(false);
    expect(isIosSafari("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0 Safari/537.36")).toBe(false);
  });
});
