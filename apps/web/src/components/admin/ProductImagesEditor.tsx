"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useState, type ChangeEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { Button } from "@/components/ui/Button";
import { sendJson, uploadFile } from "@/lib/account/client";
import { problemMessage, type ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { imageVariant } from "@/lib/media/image";

export type ProductImageView = components["schemas"]["ProductImageView"];
type Texts = Dictionary["admin"];

/** The list with one image moved by <paramref name="step"/> places (no change at the ends). */
export function moveImage<T>(list: T[], index: number, step: -1 | 1): T[] {
  const target = index + step;
  if (target < 0 || target >= list.length) return list;
  const next = [...list];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}

type Props = {
  locale: string;
  number: number;
  images: ProductImageView[];
  languageNames: Record<string, string>;
  texts: Texts;
};

/**
 * A product's images: upload (several at once), order (the first is the main image), alternative text per language,
 * delete. Uploads and deletions apply at once; order and texts are saved with their own button.
 */
export function ProductImagesEditor({ locale, number, images: initial, languageNames, texts }: Props) {
  const router = useRouter();
  const [images, setImages] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const errorTexts = { ...texts.errors, ...texts.catalogErrors, ...texts.mediaErrors } as ErrorTexts;
  const base = `/api/v1/admin/products/${number}/images` as const;

  async function after() {
    await refreshStorefront(locale, ["catalog"]);
    router.refresh();
  }

  async function onFiles(event: ChangeEvent<HTMLInputElement>) {
    const files = [...(event.target.files ?? [])];
    event.target.value = "";
    setBusy(true);
    setError(null);
    setSaved(false);
    const added: ProductImageView[] = [];
    for (const file of files) {
      const result = await uploadFile<ProductImageView>(base, file);
      if (result.ok) added.push(result.data);
      else {
        setError(`${file.name}: ${problemMessage(result.problem, errorTexts)}`);
        break;
      }
    }
    setImages((current) => [...current, ...added]);
    setBusy(false);
    if (added.length > 0) await after();
  }

  async function remove(id: string) {
    setBusy(true);
    setError(null);
    const result = await sendJson("DELETE", `${base}/${id}`);
    setBusy(false);
    if (!result.ok) return setError(problemMessage(result.problem, errorTexts));
    setImages((current) => current.filter((i) => i.id !== id));
    await after();
  }

  async function save() {
    setBusy(true);
    setError(null);
    setSaved(false);
    const result = await sendJson("PUT", base, { images: images.map((i) => ({ id: i.id, alts: i.alts })) });
    setBusy(false);
    if (!result.ok) return setError(problemMessage(result.problem, errorTexts));
    setSaved(true);
    await after();
  }

  function setAlt(id: string, locale: string, alt: string) {
    setImages((current) => current.map((i) => (i.id === id ? { ...i, alts: i.alts.map((a) => (a.locale === locale ? { ...a, alt } : a)) } : i)));
  }

  return (
    <div className="flex flex-col gap-4">
      {error && <FormAlert kind="error">{error}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.saved}</FormAlert>}
      <ul className="flex flex-col gap-3">
        {images.map((image, index) => (
          <li key={image.id} className="flex flex-col gap-3 rounded-xl border border-border p-3 sm:flex-row">
            <div className="flex shrink-0 flex-col items-center gap-2">
              <span className="h-28 w-28 overflow-hidden rounded-lg bg-surface-muted">
                <Image src={imageVariant(image.url, 300)} alt="" width={112} height={112} unoptimized className="h-full w-full object-contain" />
              </span>
              {index === 0 && <span className="rounded-md bg-primary px-2 text-xs text-primary-foreground">{texts.imageMain}</span>}
            </div>
            <fieldset className="flex flex-1 flex-col gap-2">
              <legend className="text-sm font-bold">{texts.imageAlt}</legend>
              {image.alts.map((alt) => (
                <label key={alt.locale} lang={alt.locale} className="flex flex-col gap-1 text-sm">
                  <span className="text-xs text-muted">{languageNames[alt.locale] ?? alt.locale}</span>
                  <input
                    value={alt.alt}
                    onChange={(e) => setAlt(image.id, alt.locale, e.target.value)}
                    maxLength={300}
                    className="h-10 rounded-lg border border-border bg-surface px-3"
                  />
                </label>
              ))}
            </fieldset>
            <div className="flex shrink-0 flex-row gap-1 sm:flex-col">
              <Button variant="ghost" size="sm" onClick={() => setImages(moveImage(images, index, -1))} disabled={busy || index === 0}>
                {texts.imageMoveUp}
              </Button>
              <Button variant="ghost" size="sm" onClick={() => setImages(moveImage(images, index, 1))} disabled={busy || index === images.length - 1}>
                {texts.imageMoveDown}
              </Button>
              <Button variant="danger" size="sm" onClick={() => remove(image.id)} disabled={busy}>
                {texts.remove}
              </Button>
            </div>
          </li>
        ))}
      </ul>
      <div className="flex flex-wrap items-center gap-2">
        <label className="flex h-11 cursor-pointer items-center rounded-xl border border-dashed border-border px-4 text-sm hover:border-primary">
          {busy ? texts.imageUploading : `+ ${texts.imageUpload}`}
          <input type="file" multiple accept="image/jpeg,image/png,image/webp,image/gif" onChange={onFiles} disabled={busy} className="sr-only" />
        </label>
        {images.length > 0 && (
          <Button variant="secondary" onClick={save} disabled={busy}>
            {texts.imagesSave}
          </Button>
        )}
      </div>
      <span className="text-xs text-muted">{texts.imageHint}</span>
    </div>
  );
}
