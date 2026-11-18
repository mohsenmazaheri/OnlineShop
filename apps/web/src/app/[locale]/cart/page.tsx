import type { Metadata } from "next";
import { Suspense } from "react";
import { RequestTimeMarker } from "@/components/RequestTimeMarker";
import { Icon } from "@/components/ui/Icon";
import { ButtonLink } from "@/components/ui/Button";
import { routeLocale } from "@/lib/account/page-helpers";
import { getDictionary } from "@/lib/i18n/dictionaries";

// The cart is private to the visitor: never indexed (robots.txt disallows /*/cart as well).
export async function generateMetadata({ params }: PageProps<"/[locale]/cart">): Promise<Metadata> {
  const dict = await getDictionary(routeLocale((await params).locale));
  return { title: dict.cart.title, robots: { index: false, follow: false } };
}

/** Placeholder until the cart API (phase 7): the header and bottom bar already link here. */
export default async function CartPage({ params }: PageProps<"/[locale]/cart">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-md flex-1 flex-col items-center justify-center gap-4 px-4 py-12 text-center">
      <span className="tint tint-0 flex h-20 w-20 items-center justify-center rounded-3xl">
        <Icon name="cart" size={36} />
      </span>
      <h1 className="text-xl font-extrabold">{dict.cart.title}</h1>
      <p className="text-muted">{dict.cart.empty}</p>
      <ButtonLink href={`/${locale}`}>{dict.cart.continue}</ButtonLink>
      <Suspense fallback={null}>
        <RequestTimeMarker />
      </Suspense>
    </main>
  );
}
