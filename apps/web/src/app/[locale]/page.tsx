import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { CategoryGrid, CategoryGridSkeleton } from "@/components/CategoryGrid";
import { Benefits } from "@/components/home/Benefits";
import { HeroBanner } from "@/components/home/HeroBanner";
import { QuickLinks } from "@/components/home/QuickLinks";
import { getCategoryMenu } from "@/lib/api/storefront";
import { isLocale, type Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { buildAlternates } from "@/lib/seo/config";
import { loadStore } from "@/lib/store/load";

export async function generateMetadata({ params }: PageProps<"/[locale]">): Promise<Metadata> {
  const { locale } = await params;
  if (!isLocale(locale)) return {};
  const [dict, store] = await Promise.all([getDictionary(locale), loadStore()]);
  return {
    title: { absolute: store.name },
    description: dict.meta.description,
    alternates: buildAlternates("/", locale, store.locales, store.defaultLocale),
    openGraph: { siteName: store.name, locale, type: "website" },
  };
}

/** The banner carries the page's h1 (the shop name), read per request. */
async function StoreHero({ locale, dict }: { locale: Locale; dict: Dictionary }) {
  const store = await loadStore();
  return (
    <HeroBanner
      title={store.name}
      text={dict.meta.description}
      badge={dict.home.heroBadge}
      cta={dict.home.heroCta}
      ctaHref={`/${locale}#categories`}
    />
  );
}

/**
 * Read per request (the build has no API), then cached by `use cache` for hours and revalidated on change.
 * Streamed in the same HTML response, so crawlers see the full content.
 */
async function Categories({ dict }: { dict: Dictionary }) {
  // Request time only: the build has no API (and the image is shared by every shop).
  await connection();
  // An unreachable API must not break the page: show a friendly notice instead.
  const categories = await getCategoryMenu().catch(() => null);
  return categories ? (
    <CategoryGrid categories={categories} heading={dict.home.categories} emptyText={dict.home.noCategories} />
  ) : (
    <p role="alert" className="text-muted">
      {dict.home.unavailable}
    </p>
  );
}

/**
 * Home page laid out after the "home page design" canvas (Digikala-style structure, our own palette and texts):
 * banner, shortcuts, categories, benefits. Product rails (flash deals with a countdown, best sellers, suggestions)
 * join once products exist (phases 4 and 6); promotional banners come with branding (phase 3d).
 */
export default async function HomePage({ params }: PageProps<"/[locale]">) {
  const { locale } = await params;
  // Unknown first segments (/favicon.png, typos) are 404s; without this check getDictionary would throw a 500.
  if (!isLocale(locale)) notFound();
  const dict = await getDictionary(locale);
  const t = dict.home;

  return (
    <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col gap-7 px-4 pt-4 md:gap-10 md:px-6 md:pt-6">
      <Suspense fallback={<div className="min-h-44 animate-pulse rounded-[calc(var(--radius)*1.75)] bg-border md:min-h-80" />}>
        <StoreHero locale={locale} dict={dict} />
      </Suspense>
      <QuickLinks
        label={t.quickAccess}
        links={[
          { href: `/${locale}#categories`, label: t.quickCategories, icon: "grid" },
          { href: `/${locale}/cart`, label: t.quickCart, icon: "cart" },
          { href: `/${locale}/account`, label: t.quickAccount, icon: "user" },
          { href: `/${locale}/account/addresses`, label: t.quickAddresses, icon: "pin" },
        ]}
      />
      <Suspense fallback={<CategoryGridSkeleton label={t.loading} />}>
        <Categories dict={dict} />
      </Suspense>
      <Benefits
        label={t.benefits}
        items={[
          { icon: "lock", title: t.benefitPaymentTitle, text: t.benefitPaymentText },
          { icon: "phone", title: t.benefitMobileTitle, text: t.benefitMobileText },
          { icon: "shield", title: t.benefitPrivacyTitle, text: t.benefitPrivacyText },
          { icon: "truck", title: t.benefitDeliveryTitle, text: t.benefitDeliveryText },
        ]}
      />
    </main>
  );
}
