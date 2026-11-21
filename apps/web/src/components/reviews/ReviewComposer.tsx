"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import { Icon } from "@/components/ui/Icon";
import type { ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";

type State = components["schemas"]["MyReviewState"];
type Mine = components["schemas"]["MyReview"];
type Texts = Dictionary["reviews"];

type Props = {
  locale: string;
  number: number;
  /** The product page's address, to come back to after signing in. */
  returnPath: string;
  texts: Texts;
  errors: ErrorTexts;
};

/**
 * "Write a review": asks the API only when opened (whether the visitor is signed in and may review, and their earlier
 * review), so product pages make no extra request. A guest goes to sign-in and comes back here; an account without a
 * confirmed email or mobile number is told so. Writing again replaces the review and sends it back to moderation.
 */
export function ReviewComposer({ locale, number, returnPath, texts, errors }: Props) {
  const router = useRouter();
  const [state, setState] = useState<State | null>(null);
  const [open, setOpen] = useState(false);
  const [rating, setRating] = useState(0);
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const { busy, submit, formError, fieldError } = useApiForm(errors);
  const path = `/api/v1/account/reviews/${number}` as const;

  async function start() {
    setNotice(null);
    const response = await fetch(path, { credentials: "same-origin", headers: { Accept: "application/json" } }).catch(() => null);
    const answer = response?.ok ? ((await response.json()) as State) : null;
    if (answer && !answer.signedIn) {
      router.push(`/${locale}/account/login?returnUrl=${encodeURIComponent(`${returnPath}#reviews`)}`);
      return;
    }
    setState(answer);
    fill(answer?.review ?? null);
    setOpen(true);
  }

  function fill(review: Mine | null) {
    setRating(review?.rating ?? 0);
    setTitle(review?.title ?? "");
    setBody(review?.body ?? "");
  }

  async function send(event: FormEvent) {
    event.preventDefault();
    const result = await submit<Mine>(`${path}?locale=${locale}`, { rating, title: title.trim() || null, body }, "PUT");
    if (!result.ok) return;
    setState((s) => (s ? { ...s, review: result.data } : s));
    setOpen(false);
    setNotice(texts.thanks);
  }

  async function remove() {
    const result = await submit<unknown>(path, undefined, "DELETE");
    if (!result.ok) return;
    setState((s) => (s ? { ...s, review: null } : s));
    fill(null);
    setOpen(false);
    setNotice(texts.deleted);
  }

  const mine = state?.review ?? null;
  const status = mine && !open ? { Pending: texts.pending, Approved: texts.approved, Rejected: texts.rejected }[mine.status] : null;

  return (
    <div className="flex flex-col gap-3">
      {notice && <FormAlert kind="success">{notice}</FormAlert>}
      {status && <p className="text-sm text-muted">{status}</p>}
      {!open && (
        <div>
          <Button variant="secondary" onClick={start} className="gap-2">
            <Icon name="message" size={18} />
            {mine ? texts.edit : texts.write}
          </Button>
        </div>
      )}
      {open && state && !state.canReview && <FormAlert kind="error">{texts.unverified}</FormAlert>}
      {open && state?.canReview && (
        <form onSubmit={send} className="flex flex-col gap-3 rounded-2xl border border-border p-4" noValidate>
          <fieldset className="flex flex-col gap-1">
            <legend className="mb-1 text-sm font-medium">{texts.yourRating}</legend>
            <div className="flex gap-1" dir="ltr">
              {[1, 2, 3, 4, 5].map((n) => (
                <label key={n} className="cursor-pointer">
                  <input type="radio" name="rating" value={n} checked={rating === n} onChange={() => setRating(n)} className="peer sr-only" />
                  <span
                    className={`grid h-10 w-10 place-items-center rounded-lg peer-focus-visible:ring-2 peer-focus-visible:ring-ring ${n <= rating ? "text-accent" : "text-border"}`}
                  >
                    <Icon name="star" size={28} fill="currentColor" strokeWidth={0} />
                    <span className="sr-only">{texts.stars.replace("{rating}", String(n))}</span>
                  </span>
                </label>
              ))}
            </div>
            {fieldError("rating") && <span className="text-xs text-danger">{fieldError("rating")}</span>}
          </fieldset>
          <div className="flex flex-col gap-1 text-sm">
            <label htmlFor="review-title">{texts.titleLabel}</label>
            <input
              id="review-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={120}
              className="h-11 rounded-xl border border-border bg-surface px-3"
            />
            {fieldError("title") && <span className="text-xs text-danger">{fieldError("title")}</span>}
          </div>
          <div className="flex flex-col gap-1 text-sm">
            <label htmlFor="review-body">{texts.bodyLabel}</label>
            <textarea
              id="review-body"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              required
              minLength={10}
              maxLength={2000}
              rows={5}
              aria-describedby="review-hint"
              className="rounded-xl border border-border bg-surface p-3"
            />
            <span id="review-hint" className="text-xs text-muted">
              {texts.bodyHint}
            </span>
            {fieldError("body") && <span className="text-xs text-danger">{fieldError("body")}</span>}
          </div>
          {formError && <FormAlert kind="error">{formError}</FormAlert>}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={busy || rating === 0}>
              {texts.submit}
            </Button>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              {texts.cancel}
            </Button>
            {mine && (
              <Button variant="ghost" onClick={remove} disabled={busy} className="text-danger">
                {texts.delete}
              </Button>
            )}
          </div>
        </form>
      )}
    </div>
  );
}
