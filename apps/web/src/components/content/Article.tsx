import Image from "next/image";
import Link from "next/link";
import { ProductRail } from "@/components/product/ProductRail";
import type { ContentArticle } from "@/lib/api/storefront";
import { ContentBody } from "@/lib/content/markdown";
import type { DisplayCurrency } from "@/lib/currency/display";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";

type Props = { article: ContentArticle; locale: string; dict: Dictionary; money: DisplayCurrency };

/**
 * A blog post or content page, server-rendered: breadcrumb, title, (posts) author and date, cover, the text with its
 * marks, tags linking to the blog's tag lists, and the products the post is about.
 */
export function Article({ article, locale, dict, money }: Props) {
  const t = dict.blog;
  const isPost = article.kind === "Post";
  return (
    <article className="flex flex-col gap-5">
      <nav aria-label={dict.product.breadcrumb} className="text-xs text-muted">
        <ol className="flex flex-wrap items-center gap-1.5">
          <li>
            <Link href={`/${locale}`} className="hover:text-primary">
              {dict.product.home}
            </Link>
          </li>
          {isPost && (
            <>
              <li aria-hidden>/</li>
              <li>
                <Link href={`/${locale}/blog`} className="hover:text-primary">
                  {t.title}
                </Link>
              </li>
            </>
          )}
          <li aria-hidden>/</li>
          <li aria-current="page">{article.title}</li>
        </ol>
      </nav>
      <header className="flex max-w-3xl flex-col gap-2">
        <h1 className="text-2xl leading-snug font-extrabold md:text-3xl">{article.title}</h1>
        {isPost && (
          <p className="flex flex-wrap gap-2 text-sm text-muted">
            {article.authorName && <span>{t.by.replace("{author}", article.authorName)}</span>}
            {article.authorName && <span aria-hidden>·</span>}
            <time dateTime={article.publishedUtc}>{formatDate(article.publishedUtc, locale)}</time>
          </p>
        )}
        {article.summary && <p className="text-lg text-muted">{article.summary}</p>}
      </header>
      {article.coverImageUrl && (
        <Image
          src={article.coverImageUrl}
          alt=""
          width={1200}
          height={630}
          unoptimized
          priority
          className="aspect-[1200/630] w-full max-w-3xl rounded-[calc(var(--radius)*1.5)] bg-surface-muted object-cover"
        />
      )}
      <ContentBody source={article.body} />
      {article.tags.length > 0 && (
        <ul aria-label={t.tags} className="flex flex-wrap gap-2 text-sm">
          {article.tags.map((tag) => (
            <li key={tag}>
              <Link href={`/${locale}/blog?tag=${encodeURIComponent(tag)}`} className="rounded-full bg-surface-muted px-3 py-1 hover:text-primary">
                #{tag}
              </Link>
            </li>
          ))}
        </ul>
      )}
      {article.relatedProducts.length > 0 && (
        <ProductRail id="post-products" heading={t.related} products={article.relatedProducts} locale={locale} money={money} texts={dict.product} />
      )}
    </article>
  );
}
