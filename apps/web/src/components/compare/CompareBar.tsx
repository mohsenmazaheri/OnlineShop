"use client";

import { usePathname } from "next/navigation";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Icon } from "@/components/ui/Icon";
import { comparePath } from "@/lib/compare/list";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";
import { useCompare } from "./useCompare";

type Texts = Dictionary["compare"];

/**
 * The floating "Compare (3)" bar while products are on the list, above the phone's tab bar. It also asks what to do
 * when an add did not happen (the list is full, or the product is from another category). Not shown on the comparison
 * page itself or in the admin panel. Reads the URL, so the layout renders it inside <Suspense>.
 */
export function CompareBar({ locale, texts }: { locale: string; texts: Texts }) {
  const pathname = usePathname();
  const { state, store } = useCompare();
  if (pathname?.startsWith(`/${locale}/admin`) || pathname?.startsWith(`/${locale}/compare`)) return null;
  const { list, notice } = state;
  if (list.length === 0 && !notice) return null;

  return (
    <aside
      aria-label={texts.barLabel}
      className="pointer-events-none fixed inset-x-0 bottom-[calc(4.25rem+env(safe-area-inset-bottom))] z-40 px-4 md:bottom-4"
    >
      <div className="pointer-events-auto mx-auto flex max-w-xl flex-col gap-3 rounded-2xl border border-border bg-surface p-3 text-sm shadow-lg">
        {notice && (
          <div role="alert" className="flex flex-col gap-2">
            <p>{notice.kind === "full" ? texts.full : texts.otherCategory}</p>
            <div className="flex flex-wrap gap-2">
              {notice.kind === "otherCategory" && (
                <Button size="sm" onClick={() => store.startNew(notice.entry)}>
                  {texts.startNew}
                </Button>
              )}
              <Button size="sm" variant="secondary" onClick={() => store.dismiss()}>
                {texts.cancel}
              </Button>
            </div>
          </div>
        )}
        {list.length > 0 && (
          <div className="flex items-center gap-2">
            <ButtonLink href={comparePath(locale, list.map((e) => e.n))} className="flex-1 gap-2" rel="nofollow">
              <Icon name="compare" size={20} />
              {texts.bar.replace("{count}", formatNumber(list.length, locale))}
            </ButtonLink>
            <Button variant="secondary" onClick={() => store.clear()} aria-label={texts.clear} title={texts.clear}>
              <Icon name="close" size={18} />
            </Button>
          </div>
        )}
      </div>
    </aside>
  );
}
