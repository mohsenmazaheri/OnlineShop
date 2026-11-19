"use client";

import { useRouter } from "next/navigation";
import { useState, type ChangeEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import { uploadFileWithProgress } from "@/lib/account/client";
import { problemMessage, type ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { ImageUploadField } from "./ImageUploadField";
import { moveImage } from "./ProductImagesEditor";

export type ProductVideoView = components["schemas"]["ProductVideoView"];
type Texts = Dictionary["admin"];

export const maxVideos = 3;

/** "1:05" or "1:02:03" for a duration in seconds. */
export function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds)) return "";
  const total = Math.round(seconds);
  const [h, m, s] = [Math.floor(total / 3600), Math.floor((total % 3600) / 60), total % 60];
  const pad = (n: number) => String(n).padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

/** "12.4 MB" (decimal megabytes, as file managers show). */
export function formatSize(bytes: number, locale: string): string {
  return `${(bytes / 1_000_000).toLocaleString(locale, { maximumFractionDigits: 1 })} MB`;
}

type Props = {
  locale: string;
  number: number;
  videos: ProductVideoView[];
  languageNames: Record<string, string>;
  texts: Texts;
};

/**
 * A product's videos: upload an MP4 (with progress), a poster image, titles per language, order and removal. The
 * shop shows them in the product gallery after the images.
 */
export function ProductVideosEditor({ locale, number, videos: initial, languageNames, texts }: Props) {
  const router = useRouter();
  const errorTexts = { ...texts.errors, ...texts.catalogErrors, ...texts.mediaErrors } as ErrorTexts;
  const { busy, submit, formError } = useApiForm(errorTexts);
  const [videos, setVideos] = useState(initial);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function done() {
    await refreshStorefront(locale, ["catalog"]);
    router.refresh();
  }

  async function upload(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    setError(null);
    setProgress(0);
    const result = await uploadFileWithProgress<ProductVideoView>(`/api/v1/admin/products/${number}/videos`, file, setProgress);
    setProgress(null);
    if (!result.ok) {
      setError(problemMessage(result.problem, errorTexts));
      return;
    }
    setVideos((current) => [...current, result.data]);
    await done();
  }

  async function remove(id: string) {
    if (!window.confirm(texts.confirmDeleteVideo)) return;
    if ((await submit(`/api/v1/admin/products/${number}/videos/${id}`, undefined, "DELETE")).ok) {
      setVideos((current) => current.filter((v) => v.id !== id));
      await done();
    }
  }

  async function save() {
    setSaved(false);
    const body = { videos: videos.map((v) => ({ id: v.id, titles: v.titles })) };
    if ((await submit(`/api/v1/admin/products/${number}/videos`, body, "PUT")).ok) {
      setSaved(true);
      await done();
    }
  }

  const setTitle = (id: string, locale: string, title: string) =>
    setVideos(videos.map((v) => (v.id === id ? { ...v, titles: v.titles.map((t) => (t.locale === locale ? { ...t, title } : t)) } : v)));

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted">{texts.videosIntro}</p>
      {(error || formError) && <FormAlert kind="error">{error ?? formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.saved}</FormAlert>}

      {videos.length === 0 && <p className="text-sm text-muted">{texts.videoNone}</p>}
      <ul className="flex flex-col gap-3">
        {videos.map((video, i) => (
          <li key={video.id} className="flex flex-col gap-3 rounded-xl border border-border p-3 md:flex-row">
            <video src={video.url} poster={video.posterUrl ?? undefined} controls preload="metadata" className="aspect-video w-full rounded-lg bg-black md:w-64" />
            <div className="flex flex-1 flex-col gap-2">
              <p className="text-xs text-muted">
                {[formatDuration(video.durationSeconds), formatSize(video.sizeBytes, locale)].filter(Boolean).join(" · ")}
              </p>
              {video.titles.map((t) => (
                <label key={t.locale} lang={t.locale} className="flex flex-col gap-1 text-sm">
                  <span>{`${texts.videoTitle} (${languageNames[t.locale] ?? t.locale})`}</span>
                  <input
                    value={t.title}
                    onChange={(e) => setTitle(video.id, t.locale, e.target.value)}
                    maxLength={200}
                    className="h-10 rounded-xl border border-border bg-surface px-3 text-sm"
                  />
                </label>
              ))}
              <ImageUploadField
                locale={locale}
                label={texts.videoPoster}
                currentUrl={video.posterUrl}
                uploadPath={`/api/v1/admin/products/${number}/videos/${video.id}/poster`}
                removePath={`/api/v1/admin/products/${number}/videos/${video.id}/poster`}
                texts={texts}
                errorTexts={errorTexts}
                refresh={["catalog"]}
              />
              <div className="flex flex-wrap gap-2">
                <Button variant="ghost" size="sm" onClick={() => setVideos(moveImage(videos, i, -1))} disabled={i === 0}>
                  {texts.imageMoveUp}
                </Button>
                <Button variant="ghost" size="sm" onClick={() => setVideos(moveImage(videos, i, 1))} disabled={i === videos.length - 1}>
                  {texts.imageMoveDown}
                </Button>
                <Button variant="danger" size="sm" onClick={() => remove(video.id)} disabled={busy}>
                  {texts.remove}
                </Button>
              </div>
            </div>
          </li>
        ))}
      </ul>

      <div className="flex flex-wrap items-center gap-3">
        {videos.length < maxVideos && (
          <div className="flex flex-col gap-1 text-sm">
            <label className="flex flex-col gap-1">
              <span className="font-medium">{texts.videoUpload}</span>
              <input type="file" accept="video/mp4,.mp4" onChange={upload} disabled={progress !== null} aria-describedby="video-hint" className="text-sm" />
            </label>
            <span id="video-hint" className="text-xs text-muted">
              {texts.videoHint}
            </span>
          </div>
        )}
        {progress !== null && (
          <progress value={progress} max={1} aria-label={texts.videoUploading} className="h-2 w-48">
            {Math.round(progress * 100)}%
          </progress>
        )}
        {videos.length > 0 && (
          <Button onClick={save} disabled={busy || progress !== null}>
            {texts.videosSave}
          </Button>
        )}
      </div>
    </div>
  );
}
