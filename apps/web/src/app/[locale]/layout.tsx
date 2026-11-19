import type { Metadata, Viewport } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { ServiceWorkerRegistration } from "@/components/ServiceWorkerRegistration";
import { BottomNav, BottomNavBar } from "@/components/layout/BottomNav";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { SiteHeader } from "@/components/layout/SiteHeader";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { directionOf, isLocale, supportedLocales, type Locale } from "@/lib/i18n/config";
import { formatYear } from "@/lib/i18n/format-date";
import { brandingCss } from "@/lib/branding/style";
import { indexingAllowed, siteUrl } from "@/lib/seo/config";
import { fontVariables } from "@/lib/fonts";
import { loadStore } from "@/lib/store/load";
import { themeScript } from "@/lib/theme";
import "../globals.css";

/** lang/dir are known at build time; everything store-specific is read per request (one image for all shops). */
export function generateStaticParams() {
  return supportedLocales.map((locale) => ({ locale }));
}

export async function generateMetadata(): Promise<Metadata> {
  const store = await loadStore();
  return {
    metadataBase: new URL(siteUrl()),
    title: { default: store.name, template: `%s | ${store.name}` },
    applicationName: store.name,
    // Test/staging instances are never indexed (SEO_ALLOW_INDEXING must be "true" in production).
    robots: indexingAllowed() ? undefined : { index: false, follow: false },
  };
}

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#ffffff" },
    { media: "(prefers-color-scheme: dark)", color: "#1c1c22" },
  ],
};

/** Also the page's runtime marker: it reads store settings inside Suspense, so the static shell still prerenders. */
async function StoreServiceWorker() {
  const store = await loadStore();
  return <ServiceWorkerRegistration locales={store.locales} />;
}

/**
 * The shop's colours, corners and font as CSS variables (read per request, so one image serves every shop). React
 * hoists the <style> into <head>; nothing is written while the look is the default.
 */
async function StoreBrandingStyle() {
  const store = await loadStore();
  const css = brandingCss(store.branding);
  return css ? (
    <style href="store-branding" precedence="high">
      {css}
    </style>
  ) : null;
}

/** Store name, languages and the current year are per request (after loadStore's connection()). */
async function StoreFooter({ locale, dict }: { locale: Locale; dict: Dictionary }) {
  const store = await loadStore();
  return (
    <SiteFooter
      locale={locale}
      storeName={store.name}
      locales={store.locales}
      description={dict.meta.description}
      texts={dict.footer}
      year={formatYear(new Date(), locale)}
    />
  );
}

export default async function LocaleLayout({ children, params }: LayoutProps<"/[locale]">) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();

  const dict = await getDictionary(locale);

  return (
    // suppressHydrationWarning: the head script sets data-theme before React hydrates (that is the point).
    <html lang={locale} dir={directionOf(locale)} className={`h-full antialiased ${fontVariables}`} suppressHydrationWarning>
      <head>
        {/* Applies the saved light/dark choice before the first paint, so the page never flashes the wrong theme. */}
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      {/* Bottom padding on phones so the fixed tab bar never covers the end of the page. */}
      <body className="flex min-h-full flex-col pb-[calc(3.75rem+env(safe-area-inset-bottom))] md:pb-0">
        <Suspense fallback={null}>
          <StoreBrandingStyle />
        </Suspense>
        <SiteHeader locale={locale} texts={dict.header} />
        {children}
        <Suspense fallback={null}>
          <StoreFooter locale={locale} dict={dict} />
        </Suspense>
        {/* The bar reads the URL to mark the current tab: inside Suspense, with the unmarked bar as the fallback. */}
        <Suspense fallback={<BottomNavBar locale={locale} texts={dict.header} active={null} />}>
          <BottomNav locale={locale} texts={dict.header} />
        </Suspense>
        <Suspense fallback={null}>
          <StoreServiceWorker />
        </Suspense>
      </body>
    </html>
  );
}
