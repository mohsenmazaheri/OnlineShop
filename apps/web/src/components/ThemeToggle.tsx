"use client";

import { useEffect, useSyncExternalStore } from "react";
import { applyTheme, nextPreference, parsePreference, resolveTheme, themeStorageKey, type ThemePreference } from "@/lib/theme";

type Texts = { theme: string; themeSystem: string; themeLight: string; themeDark: string };

const darkQuery = "(prefers-color-scheme: dark)";
const changeEvent = "themepreferencechange";

/** The saved choice as an external store: other tabs (storage event) and this tab (custom event) notify it. */
function subscribe(onChange: () => void): () => void {
  window.addEventListener("storage", onChange);
  window.addEventListener(changeEvent, onChange);
  return () => {
    window.removeEventListener("storage", onChange);
    window.removeEventListener(changeEvent, onChange);
  };
}

function readPreference(): ThemePreference {
  try {
    return parsePreference(localStorage.getItem(themeStorageKey));
  } catch {
    return "system";
  }
}

function savePreference(preference: ThemePreference): void {
  try {
    if (preference === "system") localStorage.removeItem(themeStorageKey);
    else localStorage.setItem(themeStorageKey, preference);
  } catch {
    // Storage blocked: nothing to remember; the event below still updates this page.
  }
  window.dispatchEvent(new Event(changeEvent));
}

/**
 * One button cycling system → light → dark. The head script already applied the saved theme before paint; this keeps
 * it in sync, saves the new choice, and follows the operating system while the choice is "system".
 */
export function ThemeToggle({ texts }: { texts: Texts }) {
  // "system" while rendering on the server and hydrating (no mismatch); the saved value right after.
  const preference = useSyncExternalStore(subscribe, readPreference, () => "system" as ThemePreference);

  useEffect(() => {
    const media = window.matchMedia(darkQuery);
    const apply = () => applyTheme(resolveTheme(preference, media.matches));
    apply();
    if (preference !== "system") return;
    media.addEventListener("change", apply);
    return () => media.removeEventListener("change", apply);
  }, [preference]);

  const current = { system: texts.themeSystem, light: texts.themeLight, dark: texts.themeDark }[preference];
  return (
    <button
      type="button"
      onClick={() => savePreference(nextPreference(preference))}
      aria-label={`${texts.theme}: ${current}`}
      title={`${texts.theme}: ${current}`}
      className="flex h-10 w-10 items-center justify-center rounded-lg text-muted hover:bg-surface-muted hover:text-foreground"
    >
      <ThemeIcon preference={preference} />
    </button>
  );
}

// Simple outline icons drawn for this project.
function ThemeIcon({ preference }: { preference: ThemePreference }) {
  const common = { "aria-hidden": true, viewBox: "0 0 24 24", width: 20, height: 20, fill: "none", stroke: "currentColor", strokeWidth: 1.8, strokeLinecap: "round" as const, strokeLinejoin: "round" as const };
  if (preference === "light")
    return (
      <svg {...common}>
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
      </svg>
    );
  if (preference === "dark")
    return (
      <svg {...common}>
        <path d="M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5Z" />
      </svg>
    );
  return (
    <svg {...common}>
      <rect x="3" y="4" width="18" height="12" rx="2" />
      <path d="M8 20h8M12 16v4" />
    </svg>
  );
}
