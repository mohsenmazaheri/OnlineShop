// @vitest-environment jsdom
import { act, cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import { Countdown } from "./Countdown";

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-11-22T10:00:00Z"));
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("Countdown", () => {
  it("ticks down to the end, then says the offer has ended", async () => {
    render(<Countdown endsUtc="2026-11-24T10:00:03Z" locale="en" texts={en.product} />);
    // Before the first tick only the label (the server renders this too).
    expect(screen.getByRole("timer").textContent).toBe(en.product.offerEndsIn);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(screen.getByRole("timer").textContent).toBe(`${en.product.offerEndsIn}2 d00:00:03`);
    expect(screen.getByRole("timer").querySelector("time")!.getAttribute("datetime")).toBe("2026-11-24T10:00:03Z");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2 * 86_400_000 + 4000);
    });
    expect(screen.getByText(en.product.offerEnded)).toBeTruthy();
  });

  it("writes Persian digits in Persian", async () => {
    render(<Countdown endsUtc="2026-11-22T11:02:03Z" locale="fa" texts={en.product} />);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(screen.getByRole("timer").querySelector("time")!.textContent).toBe("۰۱:۰۲:۰۳");
  });
});
