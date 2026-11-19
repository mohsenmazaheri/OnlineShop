"use client";

import { productPath } from "@/lib/catalog/product-path";
import { cut, descriptionLimit, pageTitle, seoWarnings, titleLimit } from "@/lib/admin/seo-check";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";

export type SeoText = components["schemas"]["SeoText"];
type Texts = Dictionary["admin"];

type Props = {
  locales: string[];
  defaultLocale: string;
  values: SeoText[];
  /** The product's current name, slug and description per language (as typed). */
  names: Record<string, string>;
  slugs: Record<string, string>;
  descriptions: Record<string, string>;
  number?: number;
  shopName: string;
  siteUrl: string;
  imageCount: number;
  /** Images without alternative text, per language. */
  imagesWithoutAlt: Record<string, number>;
  languageNames: Record<string, string>;
  texts: Texts;
};

/**
 * Search texts per language (title, description, extra search words) with a search-result preview and warnings.
 * Fields left empty use the product's name and description.
 */
export function SeoFields({ locales, defaultLocale, values, names, slugs, descriptions, number, shopName, siteUrl, imageCount, imagesWithoutAlt, languageNames, texts }: Props) {
  const input = "h-10 w-full rounded-xl border border-border bg-surface px-3 text-sm";
  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-muted">{texts.seoIntro}</p>
      {locales.map((locale) => {
        const value = values.find((v) => v.locale === locale);
        return (
          <SeoLocale
            key={locale}
            locale={locale}
            isDefault={locale === defaultLocale}
            value={value}
            name={names[locale] ?? ""}
            slug={slugs[locale] ?? ""}
            description={descriptions[locale] ?? ""}
            number={number}
            shopName={shopName}
            siteUrl={siteUrl}
            imageCount={imageCount}
            imagesWithoutAlt={imagesWithoutAlt[locale] ?? 0}
            languageName={languageNames[locale] ?? locale}
            texts={texts}
            input={input}
          />
        );
      })}
    </div>
  );
}

type LocaleProps = {
  locale: string;
  isDefault: boolean;
  value?: SeoText;
  name: string;
  slug: string;
  description: string;
  number?: number;
  shopName: string;
  siteUrl: string;
  imageCount: number;
  imagesWithoutAlt: number;
  languageName: string;
  texts: Texts;
  input: string;
};

function SeoLocale({ locale, isDefault, value, name, slug, description, number, shopName, siteUrl, imageCount, imagesWithoutAlt, languageName, texts, input }: LocaleProps) {
  // Uncontrolled fields: the preview reads them through the form's input events (the editor passes the values back in).
  const metaTitle = value?.metaTitle ?? "";
  const metaDescription = value?.metaDescription ?? "";
  const title = pageTitle(metaTitle || name || "…", shopName);
  const shownDescription = metaDescription || description;
  const warnings = seoWarnings({ title, description: shownDescription, imageCount, imagesWithoutAlt, translated: isDefault || Boolean(name) });
  const url = `${siteUrl}${number ? productPath(locale, number, slug || "…") : `/${locale}/p/…`}`;

  return (
    <fieldset lang={locale} className="flex flex-col gap-2 rounded-xl border border-border p-3">
      <legend className="px-1 text-sm font-bold">{languageName}</legend>
      <div className="grid gap-2 md:grid-cols-2">
        <label className="flex flex-col gap-1 text-sm">
          <span>{texts.seoTitle}</span>
          <input name={`seo-title-${locale}`} defaultValue={metaTitle} placeholder={name} maxLength={200} className={input} />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>{texts.seoTags}</span>
          <input name={`seo-tags-${locale}`} defaultValue={value?.tags ?? ""} placeholder={texts.seoTagsHint} maxLength={500} className={input} />
        </label>
        <label className="flex flex-col gap-1 text-sm md:col-span-2">
          <span>{texts.seoDescription}</span>
          <textarea
            name={`seo-description-${locale}`}
            defaultValue={metaDescription}
            placeholder={description.slice(0, descriptionLimit)}
            rows={2}
            maxLength={400}
            className="rounded-xl border border-border bg-surface px-3 py-2 text-sm"
          />
        </label>
      </div>

      <figure aria-label={texts.seoPreview} className="flex flex-col gap-0.5 rounded-xl bg-background p-3" dir="auto">
        <figcaption className="text-xs text-muted">{texts.seoPreview}</figcaption>
        <span className="truncate text-xs text-muted" dir="ltr">
          {url}
        </span>
        <span className="text-base font-medium text-primary">{cut(title, titleLimit)}</span>
        <span className="text-sm text-muted">{cut(shownDescription, descriptionLimit) || "—"}</span>
      </figure>

      {warnings.length > 0 && (
        <ul className="flex flex-col gap-1 text-xs" aria-label={texts.seoWarningsTitle}>
          {warnings.map((w) => (
            <li key={w} className="flex gap-1.5 text-warning">
              <span aria-hidden>⚠</span>
              {texts.seoWarnings[w].replace("{count}", String(imagesWithoutAlt))}
            </li>
          ))}
        </ul>
      )}
    </fieldset>
  );
}

/** The search texts of every language from the form. */
export function readSeoFields(form: FormData, locales: string[]): SeoText[] {
  const value = (name: string) => {
    const v = form.get(name);
    return typeof v === "string" && v.trim() ? v.trim() : null;
  };
  return locales.map((locale) => ({
    locale,
    metaTitle: value(`seo-title-${locale}`),
    metaDescription: value(`seo-description-${locale}`),
    tags: value(`seo-tags-${locale}`),
  }));
}
