import Image from "next/image";
import Link from "next/link";
import type { components } from "@/lib/api/schema";
import { formatNumber } from "@/lib/i18n/format-money";
import type { Dictionary } from "@/lib/i18n/dictionaries";

type ProductPage = components["schemas"]["ProductPage"];
type Texts = Dictionary["admin"];

/** The admin product list: a GET search form (bookmarkable), one row per product, and page links. */
export function ProductList({ locale, texts, page, search, currency }: { locale: string; texts: Texts; page: ProductPage; search: string; currency: string }) {
  const pages = Math.max(1, Math.ceil(page.total / page.pageSize));
  const href = (p: number) => `/${locale}/admin/products?${new URLSearchParams({ ...(search ? { search } : {}), page: String(p) }).toString()}`;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <form method="get" action={`/${locale}/admin/products`} role="search" className="flex gap-2">
          <input
            type="search"
            name="search"
            defaultValue={search}
            aria-label={texts.productSearch}
            placeholder={texts.productSearch}
            className="h-11 w-64 max-w-full rounded-xl border border-border bg-surface px-3"
          />
          <button type="submit" className="h-11 rounded-xl border border-border px-4 text-sm">
            {texts.productSearchButton}
          </button>
        </form>
        <Link href={`/${locale}/admin/products/new`} className="flex h-11 items-center rounded-xl bg-primary px-4 text-sm font-bold text-primary-foreground">
          + {texts.productNew}
        </Link>
      </div>

      <p className="text-sm text-muted">{texts.productTotal.replace("{count}", formatNumber(page.total, locale))}</p>

      <ul className="flex flex-col divide-y divide-border rounded-2xl border border-border bg-surface">
        {page.items.map((item) => (
          <li key={item.number}>
            <Link href={`/${locale}/admin/products/${item.number}`} className="flex items-center gap-3 p-3 hover:bg-surface-muted">
              <span className="h-14 w-14 shrink-0 overflow-hidden rounded-lg bg-surface-muted">
                {item.imageUrl && <Image src={item.imageUrl} alt="" width={56} height={56} unoptimized className="h-full w-full object-cover" />}
              </span>
              <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="truncate font-medium">{item.name}</span>
                <span className="truncate text-xs text-muted">{item.groupPath}</span>
                <span className="flex flex-wrap gap-1 text-xs">
                  <span dir="ltr" className="text-muted">
                    #{item.number} · {item.sku}
                  </span>
                  {!item.isPublished && <span className="rounded-md bg-surface-muted px-1.5">{texts.productDraft}</span>}
                  {item.stock === 0 && <span className="rounded-md bg-danger/10 px-1.5 text-danger">{texts.productOutOfStock}</span>}
                  {item.missingLocales.length > 0 && (
                    <span className="rounded-md bg-accent px-1.5 text-accent-foreground">
                      {texts.untranslated}: {item.missingLocales.join(", ")}
                    </span>
                  )}
                </span>
              </span>
              <span className="shrink-0 text-sm font-bold" dir="ltr">
                {formatNumber(item.price, locale)} {currency}
              </span>
            </Link>
          </li>
        ))}
        {page.items.length === 0 && <li className="p-4 text-sm text-muted">{texts.productNone}</li>}
      </ul>

      {pages > 1 && (
        <nav aria-label={texts.pages} className="flex flex-wrap gap-1">
          {Array.from({ length: pages }, (_, i) => i + 1).map((p) => (
            <Link
              key={p}
              href={href(p)}
              aria-current={p === page.page ? "page" : undefined}
              className="flex h-10 min-w-10 items-center justify-center rounded-lg border border-border px-2 text-sm aria-[current=page]:bg-primary aria-[current=page]:text-primary-foreground"
            >
              {formatNumber(p, locale)}
            </Link>
          ))}
        </nav>
      )}
    </div>
  );
}
