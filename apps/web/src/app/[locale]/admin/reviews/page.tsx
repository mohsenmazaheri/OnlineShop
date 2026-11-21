import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ReviewModeration } from "@/components/admin/ReviewModeration";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, firstParam, routeLocale } from "@/lib/account/page-helpers";
import { getReviewsForModeration, type ReviewStatusName } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/reviews">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.reviewsTitle);
}

type Search = PageProps<"/[locale]/admin/reviews">["searchParams"];

const statuses: readonly ReviewStatusName[] = ["Pending", "Approved", "Rejected"];

/** Customer reviews waiting for a decision (reviews.moderate); ?status=Approved|Rejected|Pending (default), ?status=all. */
async function Reviews({ locale, dict, searchParams }: { locale: Locale; dict: Dictionary; searchParams: Search }) {
  const user = await requirePermission(locale, "reviews.moderate", `/${locale}/admin/reviews`);
  const all = await searchParams;
  const asked = firstParam(all.status);
  const status = asked === "" ? "Pending" : (statuses.find((s) => s === asked) ?? null);
  const page = Math.max(1, Number.parseInt(firstParam(all.page), 10) || 1);
  const list = (await getReviewsForModeration(status, page)) ?? { items: [], page: 1, pageSize: 25, total: 0, pendingCount: 0 };

  return (
    <AdminShell locale={locale} texts={dict.admin} current="reviews" permissions={user.permissions}>
      <p className="text-sm text-muted">{dict.admin.reviewsIntro}</p>
      <AdminCard title={dict.admin.reviewsTitle} id="reviews">
        <ReviewModeration locale={locale} texts={dict.admin} errors={{ ...dict.account.errors, ...dict.reviews.errors }} list={list} status={status} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function ReviewsPage({ params, searchParams }: PageProps<"/[locale]/admin/reviews">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Reviews locale={locale} dict={dict} searchParams={searchParams} />
    </Suspense>
  );
}
