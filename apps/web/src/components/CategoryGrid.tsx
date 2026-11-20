import Image from "next/image";
import Link from "next/link";
import { monogram } from "@/components/layout/Brand";
import type { CategorySummary } from "@/lib/api/storefront";

type Props = {
  locale: string;
  categories: CategorySummary[];
  heading: string;
  emptyText: string;
};

/**
 * Round category tiles for the home page (the target of the "categories" links in the header and bottom bar).
 * A category without an image shows the name's first letter on a soft colour. Each tile links to the category page
 * (/{locale}/c/{slug}, the slug in the reader's language).
 */
export function CategoryGrid({ locale, categories, heading, emptyText }: Props) {
  return (
    <section id="categories" aria-labelledby="categories-heading" className="flex flex-col gap-4 md:gap-5">
      <h2 id="categories-heading" className="text-lg font-extrabold md:text-center md:text-2xl">
        {heading}
      </h2>
      {categories.length === 0 ? (
        <p className="text-muted md:text-center">{emptyText}</p>
      ) : (
        <ul className="grid grid-cols-4 gap-x-2 gap-y-4 md:grid-cols-[repeat(auto-fill,minmax(7.5rem,1fr))] md:gap-x-3 md:gap-y-5">
          {categories.map((category, i) => (
            <li key={category.id} className="min-w-0">
              <Link href={`/${locale}/c/${category.slug}`} className="flex flex-col items-center gap-2 text-center hover:text-primary">
              <span
                aria-hidden
                className={`tint tint-${i % 8} flex h-16 w-16 items-center justify-center overflow-hidden rounded-[1.25rem] text-2xl font-extrabold md:h-24 md:w-24 md:rounded-[1.75rem] md:text-3xl`}
              >
                {category.imageUrl ? (
                  // Decorative (the name is the heading below); fixed size, so nothing shifts while it loads.
                  <Image src={category.imageUrl} alt="" width={96} height={96} unoptimized className="h-full w-full object-cover" />
                ) : (
                  monogram(category.name)
                )}
              </span>
              <h3 className="w-full text-xs leading-snug font-medium hyphens-auto [overflow-wrap:anywhere] md:text-sm">{category.name}</h3>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

export function CategoryGridSkeleton({ label }: { label: string }) {
  return (
    <div role="status" aria-label={label} className="grid grid-cols-4 gap-x-2 gap-y-4 md:grid-cols-[repeat(auto-fill,minmax(7.5rem,1fr))] md:gap-x-3">
      {Array.from({ length: 8 }, (_, i) => (
        <div key={i} className="flex flex-col items-center gap-2">
          <div className="h-16 w-16 animate-pulse rounded-[1.25rem] bg-border md:h-24 md:w-24 md:rounded-[1.75rem]" />
          <div className="h-3 w-14 animate-pulse rounded bg-border" />
        </div>
      ))}
    </div>
  );
}
