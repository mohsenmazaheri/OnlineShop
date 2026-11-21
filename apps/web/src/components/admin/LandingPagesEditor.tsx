"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { LandingPageList } from "@/lib/account/session";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { landingPath } from "@/lib/seo/landing";
import { LocalizedFields, readLocalizedFields } from "./LocalizedFields";
import { readSeoFields } from "./SeoFields";

type Texts = Dictionary["admin"];
type Page = LandingPageList["pages"][number];

type Props = { locale: string; texts: Texts; list: LandingPageList; languageNames: Record<string, string> };

const input = "h-11 w-full rounded-xl border border-border bg-surface px-3 text-sm";

/**
 * The landing pages: each opens into a form with its texts per language (title, address, the text above the
 * products), its search texts, the products (a listing address pasted from the shop) and whether it is published.
 */
export function LandingPagesEditor({ locale, texts, list, languageNames }: Props) {
  const [open, setOpen] = useState<string | null>(list.pages.length === 0 ? "new" : null);
  return (
    <div className="flex flex-col gap-3">
      {list.pages.length === 0 && <p className="text-sm text-muted">{texts.landingEmpty}</p>}
      <ul className="flex flex-col gap-2">
        {list.pages.map((page) => (
          <li key={page.id} className="rounded-xl border border-border">
            <button
              type="button"
              aria-expanded={open === page.id}
              onClick={() => setOpen(open === page.id ? null : page.id)}
              className="flex w-full items-center justify-between gap-2 px-3 py-3 text-start text-sm"
            >
              <span className="font-medium">{page.texts[0]?.name}</span>
              <span className="text-xs text-muted">{page.isPublished ? page.query : texts.landingDraft}</span>
            </button>
            {open === page.id && (
              <div className="border-t border-border p-3">
                <LandingForm locale={locale} texts={texts} list={list} languageNames={languageNames} page={page} onDone={() => setOpen(null)} />
              </div>
            )}
          </li>
        ))}
      </ul>
      {open === "new" ? (
        <div className="rounded-xl border border-border p-3">
          <LandingForm locale={locale} texts={texts} list={list} languageNames={languageNames} onDone={() => setOpen(null)} />
        </div>
      ) : (
        <div>
          <Button variant="secondary" onClick={() => setOpen("new")}>
            {texts.landingNew}
          </Button>
        </div>
      )}
    </div>
  );
}

function LandingForm({ locale, texts, list, languageNames, page, onDone }: Omit<Props, "list"> & { list: LandingPageList; page?: Page; onDone: () => void }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.catalogErrors } as ErrorTexts);
  const prefix = page ? `${page.id}-` : "new-";

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = {
      texts: readLocalizedFields(form, list.locales, prefix),
      seo: readSeoFields(form, list.locales).map((s) => ({ ...s, tags: null })),
      query: String(form.get("query") ?? "").trim(),
      isPublished: form.get("isPublished") === "on",
    };
    const result = await submit(page ? `/api/v1/admin/landing-pages/${page.id}` : "/api/v1/admin/landing-pages", body, page ? "PUT" : "POST");
    if (!result.ok) return;
    await refreshStorefront(locale, ["catalog"]);
    onDone();
    router.refresh();
  }

  async function remove() {
    if (!page || !window.confirm(texts.landingConfirmDelete)) return;
    const result = await submit(`/api/v1/admin/landing-pages/${page.id}`, undefined, "DELETE");
    if (!result.ok) return;
    await refreshStorefront(locale, ["catalog"]);
    router.refresh();
  }

  return (
    <form onSubmit={save} className="flex flex-col gap-3" noValidate>
      <LocalizedFields
        locales={list.locales}
        defaultLocale={list.defaultLocale}
        values={page?.texts ?? []}
        languageNames={languageNames}
        texts={texts}
        withSlug
        withDescription
        prefix={prefix}
      />
      <fieldset className="grid gap-2 sm:grid-cols-2">
        {list.locales.map((l) => {
          const seo = page?.seo.find((s) => s.locale === l);
          return (
            <div key={l} lang={l} className="flex flex-col gap-2 rounded-xl border border-border p-3 text-sm">
              <span className="font-bold">{languageNames[l] ?? l}</span>
              <label className="flex flex-col gap-1">
                <span>{texts.landingMetaTitle}</span>
                <input name={`seo-title-${l}`} defaultValue={seo?.metaTitle ?? ""} maxLength={200} className={input} />
              </label>
              <label className="flex flex-col gap-1">
                <span>{texts.landingMetaDescription}</span>
                <textarea name={`seo-description-${l}`} defaultValue={seo?.metaDescription ?? ""} maxLength={400} rows={2} className="rounded-xl border border-border bg-surface p-3" />
              </label>
            </div>
          );
        })}
      </fieldset>
      <label className="flex flex-col gap-1 text-sm">
        <span className="font-medium">{texts.landingQuery}</span>
        <input name="query" defaultValue={page?.query ?? ""} required maxLength={2000} dir="ltr" className={input} aria-describedby={`${prefix}query-hint`} />
        <span id={`${prefix}query-hint`} className="text-xs text-muted">
          {texts.landingQueryHint}
        </span>
      </label>
      <label className="flex min-h-10 items-center gap-2 text-sm">
        <input type="checkbox" name="isPublished" defaultChecked={page?.isPublished ?? true} className="h-4 w-4" />
        {texts.landingPublished}
      </label>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={busy}>
          {texts.landingSave}
        </Button>
        {page && (
          <>
            <a href={landingPath(locale, page.texts.find((t) => t.locale === locale)?.slug || page.texts[0]?.slug || "")} target="_blank" rel="noreferrer" className="inline-flex h-11 items-center px-3 text-sm text-primary hover:underline">
              {texts.landingOpen}
            </a>
            <Button variant="ghost" onClick={remove} disabled={busy} className="text-danger">
              {texts.landingDelete}
            </Button>
          </>
        )}
      </div>
    </form>
  );
}
