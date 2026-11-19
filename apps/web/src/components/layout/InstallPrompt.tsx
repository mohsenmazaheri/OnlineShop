"use client";

import { useEffect, useState } from "react";
import { Icon } from "@/components/ui/Icon";

/** The browser event that lets a page offer installation (Chromium browsers; not in every TypeScript lib). */
type InstallEvent = Event & { prompt: () => Promise<void>; userChoice: Promise<{ outcome: "accepted" | "dismissed" }> };

type Texts = { installTitle: string; installText: string; installButton: string; installIos: string; installDismiss: string };

export const dismissKey = "os_install_dismissed";

/** Safari on iPhone/iPad has no install event: there the card explains "Share → Add to Home Screen" instead. */
export function isIosSafari(userAgent: string): boolean {
  return /iPad|iPhone|iPod/.test(userAgent) && /Safari/.test(userAgent) && !/CriOS|FxiOS|EdgiOS/.test(userAgent);
}

function readDismissed(): boolean {
  try {
    return localStorage.getItem(dismissKey) === "1";
  } catch {
    return false;
  }
}

/**
 * Offers to install the shop as an app (PWA): a button where the browser supports it, instructions on iOS Safari.
 * Nothing shows when the app is already installed (standalone) or the visitor closed the card; the server renders
 * nothing either, so there is no hydration mismatch and no layout shift above the fold (the card is in the footer).
 */
export function InstallPrompt({ texts }: { texts: Texts }) {
  const [event, setEvent] = useState<InstallEvent | null>(null);
  const [mode, setMode] = useState<"hidden" | "button" | "ios">("hidden");

  useEffect(() => {
    if (readDismissed() || window.matchMedia?.("(display-mode: standalone)").matches) return;
    const onPrompt = (e: Event) => {
      e.preventDefault();
      setEvent(e as InstallEvent);
      setMode("button");
    };
    const onInstalled = () => setMode("hidden");
    window.addEventListener("beforeinstallprompt", onPrompt);
    window.addEventListener("appinstalled", onInstalled);
    // eslint-disable-next-line react-hooks/set-state-in-effect -- one-time read of the browser, not derivable during render
    if (isIosSafari(navigator.userAgent)) setMode("ios");
    return () => {
      window.removeEventListener("beforeinstallprompt", onPrompt);
      window.removeEventListener("appinstalled", onInstalled);
    };
  }, []);

  if (mode === "hidden") return null;

  function dismiss() {
    try {
      localStorage.setItem(dismissKey, "1");
    } catch {
      // Storage blocked: the card just comes back next time.
    }
    setMode("hidden");
  }

  async function install() {
    if (!event) return;
    await event.prompt();
    const { outcome } = await event.userChoice;
    if (outcome === "accepted") setMode("hidden");
  }

  return (
    <section aria-label={texts.installTitle} className="flex items-start gap-3 rounded-2xl border border-inverse-border p-3 text-white">
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-white/10 text-accent">
        <Icon name="phone" size={20} />
      </span>
      <div className="flex flex-1 flex-col gap-1">
        <strong className="text-sm">{texts.installTitle}</strong>
        <span className="text-xs text-inverse-foreground">{mode === "ios" ? texts.installIos : texts.installText}</span>
        {mode === "button" && (
          <button type="button" onClick={install} className="mt-1 self-start rounded-lg bg-accent px-3 py-1.5 text-sm font-bold text-accent-foreground">
            {texts.installButton}
          </button>
        )}
      </div>
      <button type="button" onClick={dismiss} aria-label={texts.installDismiss} className="flex h-8 w-8 items-center justify-center rounded-lg text-inverse-foreground hover:text-white">
        ×
      </button>
    </section>
  );
}
