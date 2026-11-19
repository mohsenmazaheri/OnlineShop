import type { MetadataRoute } from "next";
import { directionOf } from "@/lib/i18n/config";
import { defaultBranding, type StoreSettings } from "@/lib/store/settings";

/** Sizes of the generated app icons (src/app/icon.tsx); 192 and 512 are required for installability. */
export const iconSizes = [192, 512] as const;

/** The installed app's manifest, built per request from the store settings: name, language and the shop's colour. */
export function buildManifest(store: Pick<StoreSettings, "name" | "defaultLocale"> & Partial<Pick<StoreSettings, "branding">>): MetadataRoute.Manifest {
  const locale = store.defaultLocale;
  return {
    name: store.name,
    short_name: store.name.length > 12 ? store.name.slice(0, 12) : store.name,
    lang: locale,
    dir: directionOf(locale),
    start_url: `/${locale}`,
    scope: "/",
    display: "standalone",
    background_color: "#f5f6f4",
    theme_color: (store.branding ?? defaultBranding).primaryColor,
    icons: iconSizes.flatMap((size) => [
      { src: `/icon/${size}`, sizes: `${size}x${size}`, type: "image/png", purpose: "any" as const },
      { src: `/icon/${size}`, sizes: `${size}x${size}`, type: "image/png", purpose: "maskable" as const },
    ]),
  };
}
