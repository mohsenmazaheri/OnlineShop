import Link from "next/link";
import { Stars } from "@/components/ui/Stars";
import type { ErrorTexts } from "@/lib/account/problems";
import type { ProductReviews as ReviewsData } from "@/lib/api/storefront";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatNumber } from "@/lib/i18n/format-money";
import { ReviewComposer } from "./ReviewComposer";

type Props = {
  reviews: ReviewsData;
  number: number;
  /** The product page's path (pages of reviews are ?reviews=N on it). */
  path: string;
  locale: string;
  texts: Dictionary["reviews"];
  errors: ErrorTexts;
};

/**
 * The product's reviews, server-rendered (in the first HTML, like the rest of the page): the average and how many gave
 * each number of stars, the approved reviews newest first with real page links, and the "write a review" button.
 */
export function ProductReviews({ reviews, number, path, locale, texts, errors }: Props) {
  const { summary, items, page, pageSize, total } = reviews;
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const pageHref = (p: number) => (p === 1 ? `${path}#reviews` : `${path}?reviews=${p}#reviews`);

  return (
    <section id="reviews" aria-labelledby="reviews-heading" className="flex scroll-mt-24 flex-col gap-5">
      <h2 id="reviews-heading" className="text-lg font-extrabold">
        {texts.title}
        {summary.count > 0 && <span className="ms-2 text-sm font-normal text-muted">{texts.count.replace("{count}", formatNumber(summary.count, locale))}</span>}
      </h2>

      <div className="grid gap-6 md:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
        <div className="flex flex-col gap-4">
          {summary.count > 0 ? (
            <div className="flex flex-col gap-3 rounded-2xl border border-border p-4">
              <p className="flex items-baseline gap-2">
                <span className="text-4xl font-extrabold">{formatNumber(summary.average, locale, 1)}</span>
                <span className="text-sm text-muted">{texts.outOf}</span>
              </p>
              <Stars rating={summary.average} size={20} label={texts.stars.replace("{rating}", formatNumber(summary.average, locale, 1))} />
              <ul className="flex flex-col gap-1.5 text-xs">
                {summary.stars.map((count, i) => (
                  <li key={i} className="flex items-center gap-2">
                    <span className="w-14 shrink-0 text-muted">{texts.starRow.replace("{stars}", formatNumber(5 - i, locale))}</span>
                    <span className="h-2 flex-1 overflow-hidden rounded-full bg-surface-muted">
                      <span className="block h-full rounded-full bg-accent" style={{ width: `${(count / summary.count) * 100}%` }} />
                    </span>
                    <span className="w-8 shrink-0 text-end text-muted">{formatNumber(count, locale)}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="text-sm text-muted">{texts.none}</p>
          )}
          <ReviewComposer locale={locale} number={number} returnPath={path} texts={texts} errors={errors} />
        </div>

        {items.length > 0 && (
          <div className="flex flex-col gap-4">
            <ol className="flex flex-col divide-y divide-border">
              {items.map((review) => (
                <li key={review.id} className="flex flex-col gap-2 py-4 first:pt-0">
                  <article className="flex flex-col gap-2" lang={review.locale}>
                    <header className="flex flex-wrap items-center gap-2">
                      <Stars rating={review.rating} size={14} label={texts.stars.replace("{rating}", formatNumber(review.rating, locale))} />
                      {review.title && <h3 className="font-bold">{review.title}</h3>}
                    </header>
                    <p dir="auto" className="leading-relaxed whitespace-pre-line">
                      {review.body}
                    </p>
                    <p className="flex flex-wrap items-center gap-2 text-xs text-muted" lang={locale}>
                      <span>{review.authorName || texts.anonymous}</span>
                      <span aria-hidden>·</span>
                      <time dateTime={review.createdUtc}>{formatDate(review.createdUtc, locale)}</time>
                      {review.verifiedBuyer && <span className="rounded-full bg-success/10 px-2 py-0.5 text-success">{texts.verified}</span>}
                    </p>
                  </article>
                </li>
              ))}
            </ol>
            {pages > 1 && (
              <nav aria-label={texts.pages} className="flex gap-2 text-sm">
                {page > 1 && (
                  <Link href={pageHref(page - 1)} className="rounded-lg border border-border px-3 py-2 hover:border-primary">
                    {texts.previous}
                  </Link>
                )}
                {page < pages && (
                  <Link href={pageHref(page + 1)} className="rounded-lg border border-border px-3 py-2 hover:border-primary">
                    {texts.next}
                  </Link>
                )}
              </nav>
            )}
          </div>
        )}
      </div>
    </section>
  );
}
