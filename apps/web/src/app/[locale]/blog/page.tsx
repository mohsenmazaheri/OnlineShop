import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import { Suspense } from "react";
import { firstParam, routeLocale } from "@/lib/account/page-helpers";
import { getPosts } from "@/lib/api/storefront";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatNumber } from "@/lib/i18n/format-money";
import { buildAlternates } from "@/lib/seo/config";
import { contentPath } from "@/lib/seo/content";
import { loadStore } from "@/lib/store/load";

type Props = PageProps<"/[locale]/blog">;
type Search = Props["searchParams"];

function choices(all: Awaited<Search>) {
  const tag = firstParam(all.tag).trim().slice(0, 100) || null;
  const page = Math.min(1000, Math.max(1, Number.parseInt(firstParam(all.page), 10) || 1));
  return { tag, page };
}

/** The blog is in every shop language; a tag's list is not indexed, page 2 and on keep their own canonical. */
export async function generateMetadata({ params, searchParams }: Props): Promise<Metadata> {
  const locale = routeLocale((await params).locale);
  const [dict, store] = await Promise.all([getDictionary(locale), loadStore()]);
  const { tag, page } = choices(await searchParams);
  const alternates = buildAlternates("/blog", locale, store.locales, store.defaultLocale);
  return {
    title: tag ? dict.blog.tagTitle.replace("{tag}", tag) : dict.blog.title,
    description: dict.blog.description,
    alternates: { ...alternates, canonical: page > 1 && !tag ? `${alternates.canonical}?page=${page}` : alternates.canonical },
    robots: tag ? { index: false, follow: true } : undefined,
  };
}

async function Posts({ locale, dict, searchParams }: { locale: Locale; dict: Dictionary; searchParams: Search }) {
  await loadStore();
  const { tag, page } = choices(await searchParams);
  const list = await getPosts(locale, page, tag).catch(() => null);
  const t = dict.blog;
  if (!list)
    return (
      <p role="alert" className="text-muted">
        {dict.home.unavailable}
      </p>
    );
  const pages = Math.max(1, Math.ceil(list.total / list.pageSize));
  const href = (p: number) => {
    const query = new URLSearchParams();
    if (tag) query.set("tag", tag);
    if (p > 1) query.set("page", String(p));
    const text = query.toString();
    return `/${locale}/blog${text ? `?${text}` : ""}`;
  };

  return (
    <>
      <h1 className="text-2xl font-extrabold">{tag ? t.tagTitle.replace("{tag}", tag) : t.title}</h1>
      {list.tags.length > 0 && (
        <nav aria-label={t.tags} className="flex flex-wrap gap-2 text-sm">
          <Link href={`/${locale}/blog`} aria-current={tag ? undefined : "page"} className="rounded-full bg-surface-muted px-3 py-1 aria-[current=page]:bg-primary/10 aria-[current=page]:text-primary">
            {t.allPosts}
          </Link>
          {list.tags.map((name) => (
            <Link
              key={name}
              href={`/${locale}/blog?tag=${encodeURIComponent(name)}`}
              rel="nofollow"
              aria-current={name === tag ? "page" : undefined}
              className="rounded-full bg-surface-muted px-3 py-1 aria-[current=page]:bg-primary/10 aria-[current=page]:text-primary"
            >
              #{name}
            </Link>
          ))}
        </nav>
      )}
      {list.items.length === 0 ? (
        <p className="text-muted">{t.empty}</p>
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {list.items.map((post, i) => (
            <li key={post.slug}>
              <Link href={contentPath("Post", locale, post.slug)} className="flex h-full flex-col gap-3 rounded-2xl border border-border bg-surface p-3 hover:border-primary">
                <span className="block aspect-[1200/630] overflow-hidden rounded-xl bg-surface-muted">
                  {post.coverImageUrl && (
                    <Image src={post.coverImageUrl.replace("/1200.webp", "/600.webp")} alt="" width={600} height={315} unoptimized priority={i < 3} className="h-full w-full object-cover" />
                  )}
                </span>
                <span className="text-lg leading-snug font-bold">{post.title}</span>
                {post.summary && <span className="line-clamp-3 text-sm text-muted">{post.summary}</span>}
                <time dateTime={post.publishedUtc} className="mt-auto text-xs text-muted">
                  {formatDate(post.publishedUtc, locale)}
                </time>
              </Link>
            </li>
          ))}
        </ul>
      )}
      {pages > 1 && (
        <nav aria-label={t.pages} className="flex flex-wrap gap-1 text-sm">
          {Array.from({ length: pages }, (_, i) => i + 1).map((p) => (
            <Link
              key={p}
              href={href(p)}
              aria-current={p === list.page ? "page" : undefined}
              className="grid h-10 min-w-10 place-items-center rounded-lg border border-border px-2 aria-[current=page]:border-primary aria-[current=page]:font-bold aria-[current=page]:text-primary"
            >
              {formatNumber(p, locale)}
            </Link>
          ))}
        </nav>
      )}
    </>
  );
}

/** The blog: published posts, newest first, with their tags (docs/SEO-MARKETING.md §9). */
export default async function BlogPage({ params, searchParams }: Props) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col gap-5 px-4 py-6">
      <Suspense fallback={<h1 className="text-2xl font-extrabold">{dict.blog.title}</h1>}>
        <Posts locale={locale} dict={dict} searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
