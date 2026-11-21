import type { ContentArticle } from "@/lib/api/storefront";
import { absoluteUrl } from "./product";

type Kind = ContentArticle["kind"];

/** A post's or page's address: /{locale}/blog/{slug} or /{locale}/{slug} (encoded). */
export function contentPath(kind: Kind, locale: string, slug: string): string {
  return kind === "Post" ? `/${locale}/blog/${encodeURIComponent(slug)}` : `/${locale}/${encodeURIComponent(slug)}`;
}

/** canonical and hreflang: indexed only in a translated language; an untranslated one points at the default language. */
export function contentSeo(article: ContentArticle, locale: string, defaultLocale: string, site: string) {
  const translated = article.alternates.some((a) => a.locale === locale);
  const languages: Record<string, string> = Object.fromEntries(article.alternates.map((a) => [a.locale, `${site}${contentPath(article.kind, a.locale, a.slug)}`]));
  const fallback = article.alternates.find((a) => a.locale === defaultLocale) ?? article.alternates[0];
  if (fallback) languages["x-default"] = `${site}${contentPath(article.kind, fallback.locale, fallback.slug)}`;
  return { canonical: translated ? `${site}${contentPath(article.kind, locale, article.slug)}` : languages["x-default"], languages, indexable: translated };
}

/** BlogPosting (or WebPage) and BreadcrumbList, from what the page shows. */
export function contentJsonLd(article: ContentArticle, locale: string, site: string, pageUrl: string, crumbs: { name: string; url: string }[], publisher: string) {
  const image = article.coverImageUrl ? [absoluteUrl(site, article.coverImageUrl)] : undefined;
  const main =
    article.kind === "Post"
      ? {
          "@context": "https://schema.org",
          "@type": "BlogPosting",
          headline: article.title.slice(0, 110),
          ...(article.summary ? { description: article.summary } : {}),
          ...(image ? { image } : {}),
          datePublished: article.publishedUtc,
          dateModified: article.modifiedUtc,
          inLanguage: locale,
          mainEntityOfPage: pageUrl,
          author: article.authorName ? { "@type": "Person", name: article.authorName } : { "@type": "Organization", name: publisher },
          publisher: { "@type": "Organization", name: publisher },
          ...(article.tags.length > 0 ? { keywords: article.tags.join(", ") } : {}),
        }
      : {
          "@context": "https://schema.org",
          "@type": "WebPage",
          name: article.title,
          ...(article.summary ? { description: article.summary } : {}),
          url: pageUrl,
          inLanguage: locale,
          dateModified: article.modifiedUtc,
        };
  return [
    main,
    {
      "@context": "https://schema.org",
      "@type": "BreadcrumbList",
      itemListElement: crumbs.map((c, i) => ({ "@type": "ListItem", position: i + 1, name: c.name, item: c.url })),
    },
  ];
}
