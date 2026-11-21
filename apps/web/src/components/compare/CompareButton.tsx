"use client";

import { Icon } from "@/components/ui/Icon";
import { buttonClass } from "@/components/ui/Button";
import { comparePath } from "@/lib/compare/list";
import { useCompare, useHydrated } from "./useCompare";

export type CompareButtonTexts = { compare: string; compareAdded: string };

type Props = {
  locale: string;
  number: number;
  /** The product's category slug: adding one from another category asks first. */
  category: string;
  texts: CompareButtonTexts;
  /** "icon": the small round button on a product card; "full": icon and label (product page). */
  look?: "icon" | "full";
};

/**
 * Adds a product to the comparison list or takes it off (aria-pressed). Without JavaScript it is a link to the
 * comparison page with just this product.
 */
export function CompareButton({ locale, number, category, texts, look = "full" }: Props) {
  const hydrated = useHydrated();
  const { state, store } = useCompare();
  const active = state.list.some((e) => e.n === number);
  const label = active ? texts.compareAdded : texts.compare;
  const className =
    look === "icon"
      ? `grid h-9 w-9 place-items-center rounded-full border bg-surface/90 shadow-sm transition-colors ${active ? "border-primary text-primary" : "border-border text-muted hover:text-primary"}`
      : buttonClass("secondary", "md", active ? "gap-2 border-primary text-primary" : "gap-2");
  const content = (
    <>
      <Icon name="compare" size={look === "icon" ? 18 : 20} />
      {look === "full" && <span>{label}</span>}
    </>
  );

  if (!hydrated)
    return (
      <a href={comparePath(locale, [number])} className={className} aria-label={look === "icon" ? texts.compare : undefined} rel="nofollow">
        {content}
      </a>
    );
  return (
    <button
      type="button"
      className={className}
      aria-pressed={active}
      aria-label={look === "icon" ? texts.compare : undefined}
      title={look === "icon" ? label : undefined}
      onClick={() => store.toggle({ n: number, c: category })}
    >
      {content}
    </button>
  );
}
