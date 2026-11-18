/**
 * Light/dark theme. The visitor's choice ("system", "light" or "dark") is kept per device in localStorage; the page
 * always carries the resolved theme as <html data-theme="light|dark">, set by an inline script in <head> before the
 * first paint (no flash, and pages stay prerendered). Without JavaScript the CSS falls back to the system setting.
 */
export const themePreferences = ["system", "light", "dark"] as const;
export type ThemePreference = (typeof themePreferences)[number];
export type Theme = "light" | "dark";

export const themeStorageKey = "theme";

/** Browser chrome colour (meta theme-color) per theme; matches --surface in globals.css. */
export const themeColors: Record<Theme, string> = { light: "#ffffff", dark: "#1c1c22" };

export function parsePreference(value: string | null | undefined): ThemePreference {
  return themePreferences.includes(value as ThemePreference) ? (value as ThemePreference) : "system";
}

export function resolveTheme(preference: ThemePreference, systemPrefersDark: boolean): Theme {
  if (preference === "system") return systemPrefersDark ? "dark" : "light";
  return preference;
}

/** system → light → dark → system, for a single toggle button. */
export function nextPreference(preference: ThemePreference): ThemePreference {
  return themePreferences[(themePreferences.indexOf(preference) + 1) % themePreferences.length];
}

/**
 * The inline <head> script. Kept tiny and self-contained (it runs before any bundle), wrapped in try/catch because
 * localStorage can throw (private mode, blocked storage); the CSS then follows the system setting.
 */
export const themeScript = `(function(){try{var p=localStorage.getItem(${JSON.stringify(themeStorageKey)});var d=p==="dark"||(p!=="light"&&window.matchMedia("(prefers-color-scheme: dark)").matches);var r=document.documentElement;r.dataset.theme=d?"dark":"light";r.style.colorScheme=d?"dark":"light";}catch(e){}})();`;

/** Applies a resolved theme to the document (attribute, color-scheme and browser chrome colour). */
export function applyTheme(theme: Theme, root: HTMLElement = document.documentElement): void {
  root.dataset.theme = theme;
  root.style.colorScheme = theme;
  root.ownerDocument.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]').forEach((meta) => {
    meta.content = themeColors[theme];
  });
}
