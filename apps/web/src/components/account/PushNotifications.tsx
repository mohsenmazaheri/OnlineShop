"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { sendJson } from "@/lib/account/client";
import { problemMessage } from "@/lib/account/problems";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { keyBytes, pushSupported, stateOf, subscriptionBody, type PushState } from "@/lib/push/client";

type Texts = Dictionary["account"];

type Props = { locale: string; locales: string[]; texts: Texts };

/** The service worker that receives the notifications (the PWA's own; registered here too, e.g. in development). */
async function registration(locales: string[]): Promise<ServiceWorkerRegistration> {
  const existing = await navigator.serviceWorker.getRegistration("/");
  if (existing) return existing;
  await navigator.serviceWorker.register(`/sw.js?locales=${encodeURIComponent(locales.join(","))}`, { scope: "/" });
  return navigator.serviceWorker.ready;
}

/**
 * Notifications about the customer's orders on this device: switched on only by the customer (the browser asks for
 * permission), off again with one click. Shows nothing when the shop does not send notifications or the browser
 * cannot receive them.
 */
export function PushNotifications({ locale, locales, texts }: Props) {
  const [key, setKey] = useState<string | null>(null);
  const [state, setState] = useState<PushState>("unsupported");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!pushSupported()) return;
    let active = true;
    (async () => {
      const settings = (await fetch("/api/v1/push").then((r) => (r.ok ? r.json() : null)).catch(() => null)) as { enabled: boolean; publicKey: string | null } | null;
      if (!active || !settings?.enabled || !settings.publicKey) return;
      const existing = await navigator.serviceWorker.getRegistration("/");
      const subscription = existing ? await existing.pushManager.getSubscription() : null;
      if (!active) return;
      setKey(settings.publicKey);
      setState(stateOf(Notification.permission, subscription !== null));
    })();
    return () => {
      active = false;
    };
  }, []);

  if (!key || state === "unsupported") return null;

  async function switchOn() {
    setBusy(true);
    setError(null);
    try {
      const permission = await Notification.requestPermission();
      if (permission !== "granted") {
        setState(stateOf(permission, false));
        return;
      }
      const worker = await registration(locales);
      const subscription = await worker.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(key!) });
      const body = subscriptionBody(subscription.toJSON(), locale);
      const result = body ? await sendJson("PUT", "/api/v1/account/push", body) : null;
      if (!result?.ok) {
        await subscription.unsubscribe();
        setError(result ? problemMessage(result.problem, texts.errors) : texts.errors.generic);
        return;
      }
      setState("on");
    } catch {
      setError(texts.errors.generic);
    } finally {
      setBusy(false);
    }
  }

  async function switchOff() {
    setBusy(true);
    setError(null);
    try {
      const worker = await navigator.serviceWorker.getRegistration("/");
      const subscription = worker ? await worker.pushManager.getSubscription() : null;
      if (subscription) {
        await sendJson("POST", "/api/v1/account/push/remove", { endpoint: subscription.endpoint });
        await subscription.unsubscribe();
      }
      setState("off");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby="push" className="flex flex-col gap-2 rounded-xl border border-border p-3">
      <h2 id="push" className="font-semibold">
        {texts.pushTitle}
      </h2>
      <p className="text-sm text-muted">{state === "on" ? texts.pushOnText : state === "blocked" ? texts.pushBlocked : texts.pushIntro}</p>
      {state === "off" && (
        <div>
          <Button onClick={switchOn} disabled={busy}>
            {texts.pushTurnOn}
          </Button>
        </div>
      )}
      {state === "on" && (
        <div>
          <Button variant="secondary" onClick={switchOff} disabled={busy}>
            {texts.pushTurnOff}
          </Button>
        </div>
      )}
      {error && (
        <p role="alert" className="text-sm text-danger">
          {error}
        </p>
      )}
    </section>
  );
}
