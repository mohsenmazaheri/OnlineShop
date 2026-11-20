import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { ListingPageBody } from "@/components/listing/ListingPage";
import { decodeSegment, listingMetadata, loadListing } from "@/lib/catalog/listing-page";

type Props = PageProps<"/[locale]/b/[slug]">;

async function load({ params, searchParams }: Pick<Props, "params" | "searchParams">) {
  const { locale, slug } = await params;
  return loadListing(locale, { b: decodeSegment(slug) }, await searchParams);
}

export async function generateMetadata(props: Props): Promise<Metadata> {
  return listingMetadata(await load(props));
}

async function Brand(props: Pick<Props, "params" | "searchParams">) {
  const listing = await load(props);
  if (!listing) notFound();
  return <ListingPageBody listing={listing} heading={listing.result.scope.title ?? ""} />;
}

/** A brand's products with its description and the same filters as a category. */
export default function BrandPage({ params, searchParams }: Props) {
  return (
    <Suspense fallback={<div className="mx-auto min-h-96 w-full max-w-7xl animate-pulse px-4 py-6" />}>
      <Brand params={params} searchParams={searchParams} />
    </Suspense>
  );
}
