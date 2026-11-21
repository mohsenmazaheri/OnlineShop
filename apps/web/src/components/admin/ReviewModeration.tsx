"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import { Stars } from "@/components/ui/Stars";
import type { ErrorTexts } from "@/lib/account/problems";
import type { ReviewAdminList, ReviewStatusName } from "@/lib/account/session";
import { refreshStorefront } from "@/lib/admin/refresh";
import { productPath } from "@/lib/catalog/product-path";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatNumber } from "@/lib/i18n/format-money";

type Props = {
  locale: string;
  texts: Dictionary["admin"];
  errors: ErrorTexts;
  list: ReviewAdminList;
  status: ReviewStatusName | null;
};

const statuses: (ReviewStatusName | null)[] = ["Pending", "Approved", "Rejected", null];

/**
 * The moderation queue: filter by status (plain links), approve or reject each review. A decision updates the
 * product's rating at once and clears the storefront's cached reviews.
 */
export function ReviewModeration({ locale, texts, errors, list, status }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm(errors);
  const [done, setDone] = useState<Record<string, ReviewStatusName>>({});
  const href = (s: ReviewStatusName | null, page = 1) => {
    const query = new URLSearchParams();
    if (s) query.set("status", s);
    if (page > 1) query.set("page", String(page));
    const text = query.toString();
    return `/${locale}/admin/reviews${text ? `?${text}` : ""}`;
  };
  const pages = Math.max(1, Math.ceil(list.total / list.pageSize));

  async function decide(id: string, decision: ReviewStatusName) {
    const result = await submit(`/api/v1/admin/reviews/${id}`, { status: decision }, "PUT");
    if (!result.ok) return;
    setDone((d) => ({ ...d, [id]: decision }));
    await refreshStorefront(locale, ["reviews"]);
    router.refresh();
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm font-medium">{texts.reviewsWaiting.replace("{count}", formatNumber(list.pendingCount, locale))}</p>
      <nav aria-label={texts.reviewsTitle} className="flex flex-wrap gap-2 text-sm">
        {statuses.map((s) => (
          <Link
            key={s ?? "all"}
            href={href(s)}
            aria-current={s === status ? "page" : undefined}
            className="rounded-full border border-border px-3 py-1.5 aria-[current=page]:border-primary aria-[current=page]:text-primary"
          >
            {s ? texts.reviewStatuses[s] : texts.reviewFilterAll}
          </Link>
        ))}
      </nav>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {list.items.length === 0 ? (
        <p className="text-sm text-muted">{texts.reviewsEmpty}</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border">
          {list.items.map((r) => {
            const current = done[r.id] ?? r.status;
            return (
              <li key={r.id} className="flex flex-col gap-2 py-4">
                <div className="flex flex-wrap items-center gap-2 text-sm">
                  <Stars rating={r.rating} size={14} label={`${r.rating}/5`} />
                  {r.title && <strong>{r.title}</strong>}
                  <span className="rounded-full bg-surface-muted px-2 py-0.5 text-xs">{texts.reviewStatuses[current]}</span>
                </div>
                <p dir="auto" lang={r.locale} className="text-sm whitespace-pre-line">
                  {r.body}
                </p>
                <p className="flex flex-wrap gap-2 text-xs text-muted">
                  <span>{r.authorName || texts.reviewAnonymous}</span>
                  <span aria-hidden>·</span>
                  <time dateTime={r.createdUtc}>{formatDate(r.createdUtc, locale, true)}</time>
                  {r.verifiedBuyer && <span className="text-success">{texts.reviewVerified}</span>}
                  <span aria-hidden>·</span>
                  <Link href={productPath(locale, r.productNumber, "")} className="text-primary hover:underline">
                    {texts.reviewOn.replace("{product}", r.productName)}
                  </Link>
                </p>
                <div className="flex gap-2">
                  {current !== "Approved" && (
                    <Button size="sm" onClick={() => decide(r.id, "Approved")} disabled={busy}>
                      {texts.reviewApprove}
                    </Button>
                  )}
                  {current !== "Rejected" && (
                    <Button size="sm" variant="secondary" onClick={() => decide(r.id, "Rejected")} disabled={busy}>
                      {texts.reviewReject}
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
      {pages > 1 && (
        <nav aria-label={texts.reviewsTitle} className="flex gap-2 text-sm">
          {list.page > 1 && (
            <Link href={href(status, list.page - 1)} className="rounded-lg border border-border px-3 py-2">
              {formatNumber(list.page - 1, locale)}
            </Link>
          )}
          {list.page < pages && (
            <Link href={href(status, list.page + 1)} className="rounded-lg border border-border px-3 py-2">
              {formatNumber(list.page + 1, locale)}
            </Link>
          )}
        </nav>
      )}
    </div>
  );
}
