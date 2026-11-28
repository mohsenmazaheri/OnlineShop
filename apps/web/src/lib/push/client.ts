/**
 * Browser side of Web Push: what this browser supports, the shop's key in the form the Push API wants, and the
 * subscription as the API stores it. Pure helpers (the component does the browser calls).
 */

export type PushState = "unsupported" | "blocked" | "off" | "on";

/** Whether this browser can receive notifications at all (service workers, the Push API and notifications). */
export function pushSupported(scope: { navigator?: Navigator; PushManager?: unknown; Notification?: unknown } = globalThis): boolean {
  return Boolean(scope.navigator && "serviceWorker" in scope.navigator && scope.PushManager && scope.Notification);
}

/** A base64url key ("BEl62…") as the bytes `pushManager.subscribe` takes as applicationServerKey. */
export function keyBytes(base64Url: string): Uint8Array<ArrayBuffer> {
  const base64 = base64Url.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(base64Url.length / 4) * 4, "=");
  const binary = atob(base64);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

/** The API's request from `PushSubscription.toJSON()`; null when the browser gave no keys. */
export function subscriptionBody(json: PushSubscriptionJSON, locale: string): { endpoint: string; p256dh: string; auth: string; locale: string } | null {
  const p256dh = json.keys?.p256dh;
  const auth = json.keys?.auth;
  if (!json.endpoint || !p256dh || !auth) return null;
  return { endpoint: json.endpoint, p256dh, auth, locale };
}

/** The state to show from the permission and whether this browser holds a subscription. */
export function stateOf(permission: NotificationPermission, subscribed: boolean): PushState {
  if (permission === "denied") return "blocked";
  return subscribed && permission === "granted" ? "on" : "off";
}
