"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useState, type ChangeEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { Button } from "@/components/ui/Button";
import { sendJson, uploadFile } from "@/lib/account/client";
import { problemMessage, type ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront, type RefreshableTag } from "@/lib/admin/refresh";

export type UploadTexts = { imageUpload: string; imageUploading: string; imageHint: string; imageNone: string; remove: string };

type Props = {
  locale: string;
  label: string;
  currentUrl: string | null;
  uploadPath: `/api/v1/${string}`;
  removePath: `/api/v1/${string}`;
  texts: UploadTexts;
  errorTexts: ErrorTexts;
  /** Storefront caches to clear after a change. */
  refresh: RefreshableTag[];
};

/** One image (category tile, logo, banner): preview, upload a replacement, remove. */
export function ImageUploadField({ locale, label, currentUrl, uploadPath, removePath, texts, errorTexts, refresh }: Props) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function done() {
    await refreshStorefront(locale, refresh);
    router.refresh();
  }

  async function onFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    setBusy(true);
    setError(null);
    const result = await uploadFile(uploadPath, file);
    setBusy(false);
    if (result.ok) await done();
    else setError(problemMessage(result.problem, errorTexts));
  }

  async function remove() {
    setBusy(true);
    setError(null);
    const result = await sendJson("DELETE", removePath);
    setBusy(false);
    if (result.ok) await done();
    else setError(problemMessage(result.problem, errorTexts));
  }

  return (
    <div className="flex flex-col gap-2">
      <span className="text-sm font-bold">{label}</span>
      {error && <FormAlert kind="error">{error}</FormAlert>}
      <div className="flex flex-wrap items-center gap-3">
        <span className="flex h-20 w-20 items-center justify-center overflow-hidden rounded-xl border border-border bg-surface-muted text-xs text-muted">
          {currentUrl ? <Image src={currentUrl} alt="" width={80} height={80} unoptimized className="h-full w-full object-contain" /> : texts.imageNone}
        </span>
        <label className="flex h-11 cursor-pointer items-center rounded-xl border border-border px-4 text-sm hover:border-primary">
          {busy ? texts.imageUploading : texts.imageUpload}
          <input type="file" accept="image/jpeg,image/png,image/webp,image/gif" onChange={onFile} disabled={busy} className="sr-only" />
        </label>
        {currentUrl && (
          <Button variant="ghost" onClick={remove} disabled={busy}>
            {texts.remove}
          </Button>
        )}
      </div>
      <span className="text-xs text-muted">{texts.imageHint}</span>
    </div>
  );
}
