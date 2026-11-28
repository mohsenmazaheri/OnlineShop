import { describe, expect, it } from "vitest";
import { keyBytes, pushSupported, stateOf, subscriptionBody } from "./client";

describe("push helpers", () => {
  it("knows whether the browser can receive notifications", () => {
    const navigator = { serviceWorker: {} } as unknown as Navigator;
    expect(pushSupported({ navigator, PushManager: class {}, Notification: class {} })).toBe(true);
    expect(pushSupported({ navigator, Notification: class {} })).toBe(false);
    expect(pushSupported({ navigator: {} as Navigator, PushManager: class {}, Notification: class {} })).toBe(false);
  });

  it("turns the shop's base64url key into bytes", () => {
    expect(Array.from(keyBytes("AQID_-8"))).toEqual([1, 2, 3, 255, 239]);
  });

  it("builds the API request from the browser's subscription", () => {
    expect(subscriptionBody({ endpoint: "https://push.test/a", keys: { p256dh: "P", auth: "A" } }, "fa")).toEqual({
      endpoint: "https://push.test/a",
      p256dh: "P",
      auth: "A",
      locale: "fa",
    });
    expect(subscriptionBody({ endpoint: "https://push.test/a" }, "fa")).toBeNull();
  });

  it("shows on only with permission and a subscription", () => {
    expect(stateOf("granted", true)).toBe("on");
    expect(stateOf("granted", false)).toBe("off");
    expect(stateOf("default", true)).toBe("off");
    expect(stateOf("denied", true)).toBe("blocked");
  });
});
