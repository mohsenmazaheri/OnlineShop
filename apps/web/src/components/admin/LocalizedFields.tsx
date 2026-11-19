"use client";

import { FormField } from "@/components/account/FormParts";
import type { components } from "@/lib/api/schema";

export type LocalizedText = components["schemas"]["LocalizedText"];

type Texts = { nameLabel: string; slugLabel: string; slugHint: string; descriptionLabel: string; untranslated: string; defaultLanguage: string };

type Props = {
  /** Shop languages, the default first (the API returns them in that order). */
  locales: string[];
  defaultLocale: string;
  values: LocalizedText[];
  languageNames: Record<string, string>;
  texts: Texts;
  withSlug?: boolean;
  withDescription?: boolean;
  /** Keeps field names unique when several forms are on one page. */
  prefix?: string;
};

/**
 * One fieldset per shop language. The default language is required; another language left empty is "not translated"
 * (the shop shows the default text there) and is marked so.
 */
export function LocalizedFields({ locales, defaultLocale, values, languageNames, texts, withSlug = false, withDescription = false, prefix = "" }: Props) {
  return (
    <div className="flex flex-col gap-3">
      {locales.map((locale) => {
        const value = values.find((v) => v.locale === locale);
        const isDefault = locale === defaultLocale;
        const translated = Boolean(value?.name);
        return (
          <fieldset key={locale} lang={locale} className="flex flex-col gap-2 rounded-xl border border-border p-3">
            <legend className="flex items-center gap-2 px-1 text-sm font-bold">
              {languageNames[locale] ?? locale}
              {isDefault ? (
                <span className="rounded-md bg-surface-muted px-1.5 text-xs font-normal">{texts.defaultLanguage}</span>
              ) : (
                !translated && <span className="rounded-md bg-accent px-1.5 text-xs font-normal text-accent-foreground">{texts.untranslated}</span>
              )}
            </legend>
            <FormField label={texts.nameLabel} name={`${prefix}name-${locale}`} defaultValue={value?.name ?? ""} required={isDefault} maxLength={300} />
            {withSlug && <FormField label={texts.slugLabel} name={`${prefix}slug-${locale}`} defaultValue={value?.slug ?? ""} hint={texts.slugHint} maxLength={200} />}
            {withDescription && (
              <label className="flex flex-col gap-1 text-sm">
                <span className="font-medium">{texts.descriptionLabel}</span>
                <textarea
                  name={`${prefix}description-${locale}`}
                  defaultValue={value?.description ?? ""}
                  rows={3}
                  maxLength={10000}
                  className="rounded-xl border border-border bg-surface px-3 py-2"
                />
              </label>
            )}
          </fieldset>
        );
      })}
    </div>
  );
}

/** The texts of every language from a form built with {@link LocalizedFields}. */
export function readLocalizedFields(form: FormData, locales: string[], prefix = ""): LocalizedText[] {
  const value = (name: string) => {
    const v = form.get(`${prefix}${name}`);
    return typeof v === "string" && v.trim() ? v.trim() : null;
  };
  return locales.map((locale) => ({
    locale,
    name: value(`name-${locale}`) ?? "",
    slug: value(`slug-${locale}`),
    description: value(`description-${locale}`),
  }));
}
