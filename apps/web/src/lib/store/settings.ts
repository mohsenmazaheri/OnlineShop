import { isLocale, supportedLocales, type Locale } from "@/lib/i18n/config";
import { isMediaUrl } from "@/lib/media/image";

/** External sign-in providers this web build has buttons for (slugs from the API). */
export const externalProviders = ["google", "facebook", "microsoft"] as const;

/**
 * Store settings as the web app uses them. The API is the single source of truth (Store section of its
 * configuration); the same Docker image serves every shop because these are read at request time.
 */
export type StoreSettings = {
  name: string;
  defaultLocale: Locale;
  locales: Locale[];
  /** Base currency of every price (ISO 4217, e.g. IRR, EUR). */
  currency: string;
  /** Sign-up methods offered (phone only when an SMS provider is configured). */
  signUp: { email: boolean; phone: boolean };
  /** Sign-in methods offered (SMS codes only when an SMS provider is configured). */
  signIn: { password: boolean; phoneCode: boolean; external: string[] };
  /** Guest checkout, and the countries addresses may be in (ISO codes, upper case). */
  checkout: { guest: boolean; shippingCountries: string[] };
  /** The shop's look, applied at runtime (see lib/branding/style.ts). */
  branding: Branding;
};

export type Branding = {
  primaryColor: string;
  corners: "Sharp" | "Soft" | "Round";
  font: "Vazirmatn" | "System";
  homeLayout: "Classic" | "Catalog" | "Showcase";
  /** Uploaded logo and home banner image (media addresses only), or null. */
  logoUrl: string | null;
  heroImageUrl: string | null;
};

export const defaultBranding: Branding = { primaryColor: "#0f766e", corners: "Soft", font: "Vazirmatn", homeLayout: "Classic", logoUrl: null, heroImageUrl: null };

/** Validates API branding: anything unknown falls back to the default (the colour ends up in CSS, so it must be #rrggbb). */
export function toBranding(value: Partial<Record<keyof Branding, unknown>> | null | undefined): Branding {
  const pick = <T extends string>(v: unknown, allowed: readonly T[], fallback: T): T => (allowed.includes(v as T) ? (v as T) : fallback);
  return {
    primaryColor: typeof value?.primaryColor === "string" && /^#[0-9a-f]{6}$/i.test(value.primaryColor) ? value.primaryColor.toLowerCase() : defaultBranding.primaryColor,
    corners: pick(value?.corners, ["Sharp", "Soft", "Round"] as const, defaultBranding.corners),
    font: pick(value?.font, ["Vazirmatn", "System"] as const, defaultBranding.font),
    homeLayout: pick(value?.homeLayout, ["Classic", "Catalog", "Showcase"] as const, defaultBranding.homeLayout),
    // Only the API's own media addresses: nothing else is ever put into an <img>.
    logoUrl: isMediaUrl(value?.logoUrl as string) ? (value!.logoUrl as string) : null,
    heroImageUrl: isMediaUrl(value?.heroImageUrl as string) ? (value!.heroImageUrl as string) : null,
  };
}

export type StoreInfoLike = {
  name?: string | null;
  defaultLocale?: string | null;
  locales?: readonly string[] | null;
  currency?: string | null;
  branding?: Partial<Record<keyof Branding, unknown>> | null;
  signUp?: { email?: boolean | null; phone?: boolean | null } | null;
  signIn?: { password?: boolean | null; phoneCode?: boolean | null; external?: readonly string[] | null } | null;
  checkout?: { guest?: boolean | null; shippingCountries?: readonly string[] | null } | null;
};

/** Used when the API cannot be reached, so pages and redirects still work. */
export const fallbackStore: StoreSettings = {
  name: "Online Shop",
  defaultLocale: "fa",
  locales: [...supportedLocales],
  currency: "IRR",
  signUp: { email: true, phone: false },
  signIn: { password: true, phoneCode: false, external: [] },
  checkout: { guest: true, shippingCountries: [] },
  branding: defaultBranding,
};

/** Validates API data: drops locales this web build cannot render and keeps the default among the locales. */
export function toStoreSettings(info: StoreInfoLike | null | undefined): StoreSettings {
  if (!info) return fallbackStore;

  const name = info.name?.trim() || fallbackStore.name;
  const currency = /^[A-Z]{3}$/.test(info.currency ?? "") ? info.currency! : fallbackStore.currency;
  const signUp = { email: info.signUp?.email ?? true, phone: info.signUp?.phone ?? false };
  const signIn = {
    password: info.signIn?.password ?? true,
    phoneCode: info.signIn?.phoneCode ?? false,
    external: (info.signIn?.external ?? []).filter((p) => (externalProviders as readonly string[]).includes(p)),
  };

  const checkout = {
    guest: info.checkout?.guest ?? true,
    shippingCountries: [...new Set((info.checkout?.shippingCountries ?? []).filter((c) => /^[A-Z]{2}$/.test(c)))],
  };

  const locales = [...new Set((info.locales ?? []).filter(isLocale))];
  if (locales.length === 0) return { ...fallbackStore, name, currency, signUp, signIn, checkout, branding: toBranding(info.branding) };

  const defaultLocale = isLocale(info.defaultLocale) && locales.includes(info.defaultLocale) ? info.defaultLocale : locales[0];
  return { name, defaultLocale, locales, currency, signUp, signIn, checkout, branding: toBranding(info.branding) };
}
