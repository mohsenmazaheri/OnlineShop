import { describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import { formatOrderNumber, historyText, orderHref, statusTint } from "./format";

vi.mock("server-only", () => ({}));

import { getOrderByLink } from "./server";

const entry = (over: Partial<Parameters<typeof historyText>[0]>) => ({
  atUtc: "2026-11-27T10:00:00Z",
  kind: "Note" as const,
  status: null,
  text: null,
  isInternal: false,
  byStaff: false,
  ...over,
});

describe("order formatting", () => {
  it("words each kind of history entry", () => {
    expect(historyText(entry({ kind: "Placed" }), en.orders)).toBe(en.orders.historyPlaced);
    expect(historyText(entry({ kind: "StatusChanged", status: "Shipped" }), en.orders)).toBe("Status: Shipped");
    expect(historyText(entry({ kind: "TrackingSet", text: "Post RR123" }), en.orders)).toBe("Shipped with Post RR123");
    expect(historyText(entry({ kind: "PaymentExpired" }), en.orders)).toBe(en.orders.historyExpired);
    expect(historyText(entry({ kind: "Note", text: "Call first" }), en.orders)).toBe("Call first");
  });

  it("gives every status a tint and builds order addresses", () => {
    expect(statusTint("Pending")).toContain("tint-4");
    expect(statusTint("Canceled")).toContain("text-muted");
    expect(orderHref("fa", "order/1001?key=abc")).toBe("/fa/order/1001?key=abc");
    expect(orderHref("en", "/account/orders/1001")).toBe("/en/account/orders/1001");
    expect([formatOrderNumber(1002, "en"), formatOrderNumber(1002, "fa")]).toEqual(["1002", "۱۰۰۲"]);
  });
});

describe("getOrderByLink", () => {
  it("asks the API with the key and answers null for a refusal or no key", async () => {
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ number: 1001 })));
    expect(await getOrderByLink(1001, "a/b", fetchImpl)).toEqual({ number: 1001 });
    expect(String((fetchImpl.mock.calls[0] as unknown as [string])[0])).toContain("/api/v1/orders/1001?key=a%2Fb");

    expect(await getOrderByLink(1001, "", fetchImpl)).toBeNull();
    expect(await getOrderByLink(1001, "x", vi.fn(async () => new Response("", { status: 404 })))).toBeNull();
    expect(await getOrderByLink(1001, "x", vi.fn(async () => Promise.reject(new Error("down"))))).toBeNull();
  });
});
