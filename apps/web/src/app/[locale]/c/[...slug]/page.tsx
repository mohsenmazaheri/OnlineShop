import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { ListingPageBody } from "@/components/listing/ListingPage";
import { decodeSegment, listingMetadata, loadListing } from "@/lib/catalog/listing-page";

type Props = PageProps<"/[locale]/c/[...slug]">;

/** /{locale}/c/{category} or /{locale}/c/{category}/{sub-category}; anything deeper is not a page. */
async function load({ params, searchParams }: Pick<Props, "params" | "searchParams">) {
  const { locale, slug } = await params;
  if (slug.length > 2) return null;
  const [c, s] = slug.map(decodeSegment);
  return loadListing(locale, { c, s }, await searchParams);
}

export async function generateMetadata(props: Props): Promise<Metadata> {
  return listingMetadata(await load(props));
}

async function Category(props: Pick<Props, "params" | "searchParams">) {
  const listing = await load(props);
  if (!listing) notFound();
  return <ListingPageBody listing={listing} heading={listing.result.scope.title ?? ""} />;
}

/** A category or sub-category with its filters; the address is per language (the slug in the reader's language). */
export default function CategoryPage({ params, searchParams }: Props) {
  return (
    <Suspense fallback={<div className="mx-auto min-h-96 w-full max-w-7xl animate-pulse px-4 py-6" />}>
      <Category params={params} searchParams={searchParams} />
    </Suspense>
  );
}
