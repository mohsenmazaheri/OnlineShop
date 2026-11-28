// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import { PushNotifications } from "./PushNotifications";

const fetchMock = vi.fn();
let subscription: { endpoint: string; toJSON: () => PushSubscriptionJSON; unsubscribe: ReturnType<typeof vi.fn> } | null;
const subscribe = vi.fn();
const permission = { value: "default" as NotificationPermission };
const requestPermission = vi.fn();

function browser() {
  const registration = { pushManager: { getSubscription: async () => subscription, subscribe } };
  vi.stubGlobal("PushManager", class {});
  // A live getter: the component reads the permission after the request changed it.
  const FakeNotification = class {};
  Object.defineProperty(FakeNotification, "permission", { get: () => permission.value });
  Object.defineProperty(FakeNotification, "requestPermission", { value: requestPermission });
  vi.stubGlobal("Notification", FakeNotification);
  Object.defineProperty(navigator, "serviceWorker", {
    configurable: true,
    value: { getRegistration: async () => registration, register: vi.fn(), ready: Promise.resolve(registration) },
  });
}

const made = () => ({
  endpoint: "https://push.test/a",
  toJSON: () => ({ endpoint: "https://push.test/a", keys: { p256dh: "P", auth: "A" } }),
  unsubscribe: vi.fn(async () => true),
});

beforeEach(() => {
  fetchMock.mockReset();
  subscribe.mockReset();
  requestPermission.mockReset();
  subscription = null;
  permission.value = "default";
  fetchMock.mockImplementation(async (url: string) =>
    url === "/api/v1/push" ? new Response(JSON.stringify({ enabled: true, publicKey: "AQID" })) : new Response("true"),
  );
  vi.stubGlobal("fetch", fetchMock);
  browser();
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("PushNotifications", () => {
  it("asks for permission, subscribes and tells the shop", async () => {
    requestPermission.mockImplementation(async () => {
      permission.value = "granted";
      return "granted";
    });
    subscribe.mockImplementation(async () => (subscription = made()));
    render(<PushNotifications locale="en" locales={["fa", "en"]} texts={en.account} />);

    fireEvent.click(await screen.findByRole("button", { name: en.account.pushTurnOn }));
    expect(await screen.findByRole("button", { name: en.account.pushTurnOff })).toBeTruthy();
    expect(subscribe).toHaveBeenCalledWith({ userVisibleOnly: true, applicationServerKey: new Uint8Array([1, 2, 3]) });
    const [url, init] = fetchMock.mock.calls.find(([u]) => u === "/api/v1/account/push") as [string, RequestInit];
    expect([url, init.method, JSON.parse(String(init.body))]).toEqual(["/api/v1/account/push", "PUT", { endpoint: "https://push.test/a", p256dh: "P", auth: "A", locale: "en" }]);
  });

  it("switches off again and says when the browser blocks it", async () => {
    permission.value = "granted";
    subscription = made();
    const { unmount } = render(<PushNotifications locale="en" locales={["en"]} texts={en.account} />);
    fireEvent.click(await screen.findByRole("button", { name: en.account.pushTurnOff }));
    await waitFor(() => expect(subscription!.unsubscribe).toHaveBeenCalled());
    expect(fetchMock.mock.calls.some(([u]) => u === "/api/v1/account/push/remove")).toBe(true);
    unmount();

    permission.value = "denied";
    render(<PushNotifications locale="en" locales={["en"]} texts={en.account} />);
    expect(await screen.findByText(en.account.pushBlocked)).toBeTruthy();
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("shows nothing when the shop does not send notifications", async () => {
    fetchMock.mockImplementation(async () => new Response(JSON.stringify({ enabled: false, publicKey: null })));
    const { container } = render(<PushNotifications locale="en" locales={["en"]} texts={en.account} />);
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(container.textContent).toBe("");
  });
});
