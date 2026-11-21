import { describe, expect, it } from "vitest";
import { expiryMarginSeconds, fromLocalInput, remaining, saleAwareLife, toLocalInput } from "./sale";

const now = Date.parse("2026-11-22T10:00:00Z");

describe("saleAwareLife", () => {
  it("keeps the usual profile unless an offer ends sooner", () => {
    expect(saleAwareLife([], "hours", now)).toBe("hours");
    expect(saleAwareLife([null, "2026-11-23T10:00:00Z"], "hours", now)).toBe("hours");
    expect(saleAwareLife(["2026-11-22T10:10:00Z", "2026-11-22T10:30:00Z"], "hours", now)).toEqual({
      stale: 30,
      revalidate: 600 + expiryMarginSeconds,
      expire: 600 + expiryMarginSeconds + 60,
    });
    expect(saleAwareLife(["2026-11-22T10:10:00Z"], "minutes", now)).toBe("minutes");
  });

  it("waits for the API to end an offer that is just over", () => {
    const justOver = saleAwareLife(["2026-11-22T09:59:30Z"], "hours", now);
    expect(justOver).toEqual({ stale: 30, revalidate: 60, expire: 120 });
    expect(saleAwareLife(["2026-11-22T09:00:00Z", "not a date"], "hours", now)).toBe("hours");
  });
});

describe("remaining", () => {
  it("splits the time left into days, hours, minutes and seconds", () => {
    expect(remaining("2026-11-24T12:34:56Z", now)).toEqual({ days: 2, hours: 2, minutes: 34, seconds: 56 });
    expect(remaining("2026-11-22T10:00:00Z", now)).toBeNull();
    expect(remaining("nonsense", now)).toBeNull();
  });
});

describe("datetime-local fields", () => {
  it("round-trips through the browser's time zone", () => {
    const iso = "2026-11-30T18:00:00.000Z";
    expect(fromLocalInput(toLocalInput(iso))).toBe(iso);
    expect(toLocalInput(null)).toBe("");
    expect(fromLocalInput("")).toBeNull();
    expect(fromLocalInput("garbage")).toBeNull();
  });
});
