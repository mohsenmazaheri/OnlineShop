"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import { useHydrated } from "@/components/ui/useHydrated";
import { uploadFile } from "@/lib/account/client";
import { problemMessage, type ErrorTexts } from "@/lib/account/problems";
import type { ContentList } from "@/lib/account/session";
import { refreshStorefront } from "@/lib/admin/refresh";
import { fromLocalInput, toLocalInput } from "@/lib/catalog/sale";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { contentPath } from "@/lib/seo/content";

type Texts = Dictionary["admin"];
type Item = ContentList["items"][number];
type Kind = Item["kind"];

type Props = { locale: string; texts: Texts; list: ContentList; languageNames: Record<string, string> };

const input = "h-11 w-full rounded-xl border border-border bg-surface px-3 text-sm";
const area = "w-full rounded-xl border border-border bg-surface p-3 text-sm";

/** The form's texts per language (an empty title in another language: not translated). */
export function readContentTexts(form: FormData, locales: string[], prefix: string) {
  const value = (name: string) => {
    const v = form.get(`${prefix}${name}`);
    return typeof v === "string" && v.trim() ? v.trim() : null;
  };
  return locales.map((locale) => ({
    locale,
    title: value(`title-${locale}`) ?? "",
    slug: value(`slug-${locale}`),
    summary: value(`summary-${locale}`),
    body: value(`body-${locale}`) ?? "",
    metaTitle: value(`metaTitle-${locale}`),
    metaDescription: value(`metaDescription-${locale}`),
    tags: value(`tags-${locale}`),
  }));
}

/** "1001, 1002 x" → [1001, 1002]: the product numbers a post is about. */
export function parseNumbers(text: string): number[] {
  return [...new Set(text.split(/[\s,،]+/).map(Number).filter((n) => Number.isSafeInteger(n) && n > 0))].slice(0, 12);
}

/**
 * Blog posts and content pages: each opens into a form with its texts per language (title, address, summary, the text
 * with its simple marks, search texts, tags), the author, the products it is about, publication and cover image.
 */
export function ContentEditor({ locale, texts, list, languageNames }: Props) {
  const [open, setOpen] = useState<string | null>(list.items.length === 0 ? "new" : null);
  return (
    <div className="flex flex-col gap-3">
      {list.items.length === 0 && <p className="text-sm text-muted">{texts.contentEmpty}</p>}
      <ul className="flex flex-col gap-2">
        {list.items.map((item) => (
          <li key={item.id} className="rounded-xl border border-border">
            <button
              type="button"
              aria-expanded={open === item.id}
              onClick={() => setOpen(open === item.id ? null : item.id)}
              className="flex w-full flex-wrap items-center justify-between gap-2 px-3 py-3 text-start text-sm"
            >
              <span className="flex items-center gap-2">
                <span className="rounded-md bg-surface-muted px-1.5 text-xs">{texts.contentKinds[item.kind]}</span>
                <span className="font-medium">{item.texts[0]?.title}</span>
              </span>
              <span className="text-xs text-muted">{item.isPublished ? texts.contentPublished : texts.contentDraft}</span>
            </button>
            {open === item.id && (
              <div className="border-t border-border p-3">
                <ContentForm locale={locale} texts={texts} list={list} languageNames={languageNames} item={item} onDone={() => setOpen(null)} />
              </div>
            )}
          </li>
        ))}
      </ul>
      {open === "new" ? (
        <div className="rounded-xl border border-border p-3">
          <ContentForm locale={locale} texts={texts} list={list} languageNames={languageNames} onDone={() => setOpen(null)} />
        </div>
      ) : (
        <div>
          <Button variant="secondary" onClick={() => setOpen("new")}>
            {texts.contentNew}
          </Button>
        </div>
      )}
    </div>
  );
}

function ContentForm({ locale, texts, list, languageNames, item, onDone }: Props & { item?: Item; onDone: () => void }) {
  const router = useRouter();
  const hydrated = useHydrated();
  const errors = { ...texts.errors, ...texts.contentErrors } as ErrorTexts;
  const { busy, submit, formError } = useApiForm(errors);
  const [kind, setKind] = useState<Kind>(item?.kind ?? "Post");
  const [uploadError, setUploadError] = useState<string | null>(null);
  const prefix = item ? `${item.id}-` : "new-";

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = {
      kind,
      texts: readContentTexts(form, list.locales, prefix),
      authorName: String(form.get("authorName") ?? "").trim() || null,
      relatedProducts: parseNumbers(String(form.get("relatedProducts") ?? "")),
      isPublished: form.get("isPublished") === "on",
      publishedUtc: fromLocalInput(String(form.get("publishedAt") ?? "")),
      showInFooter: form.get("showInFooter") === "on",
    };
    const result = await submit(item ? `/api/v1/admin/content/${item.id}` : "/api/v1/admin/content", body, item ? "PUT" : "POST");
    if (!result.ok) return;
    await refreshStorefront(locale, ["content"]);
    onDone();
    router.refresh();
  }

  async function remove() {
    if (!item || !window.confirm(texts.contentConfirmDelete)) return;
    const result = await submit(`/api/v1/admin/content/${item.id}`, undefined, "DELETE");
    if (!result.ok) return;
    await refreshStorefront(locale, ["content"]);
    router.refresh();
  }

  async function upload(file: File | undefined) {
    if (!item || !file) return;
    setUploadError(null);
    const result = await uploadFile(`/api/v1/admin/content/${item.id}/cover`, file);
    if (!result.ok) {
      setUploadError(problemMessage(result.problem, { ...errors, ...texts.mediaErrors } as ErrorTexts));
      return;
    }
    await refreshStorefront(locale, ["content"]);
    router.refresh();
  }

  const slugOf = item?.texts.find((t) => t.locale === locale)?.slug || item?.texts[0]?.slug;

  return (
    <form onSubmit={save} className="flex flex-col gap-3" noValidate>
      <label className="flex max-w-xs flex-col gap-1 text-sm">
        <span>{texts.contentKind}</span>
        <select value={kind} onChange={(e) => setKind(e.target.value as Kind)} className={input}>
          <option value="Post">{texts.contentKinds.Post}</option>
          <option value="Page">{texts.contentKinds.Page}</option>
        </select>
      </label>
      {list.locales.map((l, i) => {
        const text = item?.texts.find((t) => t.locale === l);
        const name = (field: string) => `${prefix}${field}-${l}`;
        return (
          <fieldset key={l} lang={l} className="grid gap-2 rounded-xl border border-border p-3 text-sm sm:grid-cols-2">
            <legend className="px-1 font-bold">
              {languageNames[l] ?? l}
              {i > 0 && !text?.title && <span className="ms-2 rounded-md bg-accent px-1.5 text-xs font-normal text-accent-foreground">{texts.untranslated}</span>}
            </legend>
            <label className="flex flex-col gap-1">
              <span>{texts.contentTitleLabel}</span>
              <input name={name("title")} defaultValue={text?.title ?? ""} required={i === 0} maxLength={300} className={input} />
            </label>
            <label className="flex flex-col gap-1">
              <span>{texts.slugLabel}</span>
              <input name={name("slug")} defaultValue={text?.slug ?? ""} maxLength={200} className={input} />
            </label>
            <label className="flex flex-col gap-1 sm:col-span-2">
              <span>{texts.contentSummary}</span>
              <textarea name={name("summary")} defaultValue={text?.summary ?? ""} maxLength={1000} rows={2} className={area} />
            </label>
            <label className="flex flex-col gap-1 sm:col-span-2">
              <span>{texts.contentBody}</span>
              <textarea name={name("body")} defaultValue={text?.body ?? ""} required={i === 0} maxLength={100_000} rows={10} className={area} />
              <span className="text-xs text-muted">{texts.contentBodyHint}</span>
            </label>
            <label className="flex flex-col gap-1">
              <span>{texts.landingMetaTitle}</span>
              <input name={name("metaTitle")} defaultValue={text?.metaTitle ?? ""} maxLength={200} className={input} />
            </label>
            <label className="flex flex-col gap-1">
              <span>{texts.landingMetaDescription}</span>
              <input name={name("metaDescription")} defaultValue={text?.metaDescription ?? ""} maxLength={400} className={input} />
            </label>
            {kind === "Post" && (
              <label className="flex flex-col gap-1 sm:col-span-2">
                <span>{texts.contentTags}</span>
                <input name={name("tags")} defaultValue={text?.tags ?? ""} maxLength={500} className={input} />
              </label>
            )}
          </fieldset>
        );
      })}
      <div className="grid gap-3 text-sm sm:grid-cols-3">
        <label className="flex flex-col gap-1">
          <span>{texts.contentAuthor}</span>
          <input name="authorName" defaultValue={item?.authorName ?? ""} maxLength={100} className={input} />
        </label>
        <label className="flex flex-col gap-1">
          <span>{texts.contentProducts}</span>
          <input name="relatedProducts" defaultValue={item?.relatedProducts.join(", ") ?? ""} dir="ltr" className={input} />
        </label>
        <label className="flex flex-col gap-1">
          <span>{texts.contentPublishedAt}</span>
          {/* The browser's time zone: filled in once hydrated. */}
          <input key={hydrated ? "local" : "server"} type="datetime-local" name="publishedAt" defaultValue={hydrated ? toLocalInput(item?.publishedUtc) : ""} dir="ltr" className={input} />
        </label>
      </div>
      <div className="flex flex-wrap gap-4 text-sm">
        <label className="flex min-h-10 items-center gap-2">
          <input type="checkbox" name="isPublished" defaultChecked={item?.isPublished ?? false} className="h-4 w-4" />
          {texts.contentPublished}
        </label>
        {kind === "Page" && (
          <label className="flex min-h-10 items-center gap-2">
            <input type="checkbox" name="showInFooter" defaultChecked={item?.showInFooter ?? false} className="h-4 w-4" />
            {texts.contentInFooter}
          </label>
        )}
      </div>
      {item && (
        <div className="flex flex-col gap-1 text-sm">
          <span>{texts.contentCover}</span>
          {item.coverImageUrl && <span className="text-xs break-all text-muted" dir="ltr">{item.coverImageUrl}</span>}
          <input type="file" accept="image/jpeg,image/png,image/webp" aria-label={texts.contentCover} onChange={(e) => upload(e.target.files?.[0])} />
          {uploadError && <FormAlert kind="error">{uploadError}</FormAlert>}
        </div>
      )}
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={busy}>
          {texts.landingSave}
        </Button>
        {item && slugOf && (
          <a href={contentPath(item.kind, locale, slugOf)} target="_blank" rel="noreferrer" className="inline-flex h-11 items-center px-3 text-sm text-primary hover:underline">
            {texts.landingOpen}
          </a>
        )}
        {item && (
          <Button variant="ghost" onClick={remove} disabled={busy} className="text-danger">
            {texts.landingDelete}
          </Button>
        )}
      </div>
    </form>
  );
}
