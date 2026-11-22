// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { currentCount, readCount, subscribeCount, writeCount } from "./count";

afterEach(() => {
  document.cookie = "os_cart_n=; path=/; max-age=0";
});

describe("cart count cookie", () => {
  it("reads the count from a cookie header", () => {
    expect(readCount("a=1; os_cart_n=3; b=2")).toBe(3);
    expect(readCount("os_cart_n=12")).toBe(12);
    expect(readCount("xos_cart_n=5")).toBe(0);
    expect(readCount("os_cart_n=abc")).toBe(0);
    expect(readCount("")).toBe(0);
  });

  it("writes the count and tells the badges, but only when it changed", () => {
    const listener = vi.fn();
    const unsubscribe = subscribeCount(listener);

    writeCount(4);
    expect(currentCount()).toBe(4);
    writeCount(4);
    expect(listener).toHaveBeenCalledTimes(1);

    writeCount(0);
    expect(currentCount()).toBe(0);
    expect(listener).toHaveBeenCalledTimes(2);
    unsubscribe();
  });
});
