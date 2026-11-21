"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";

type Status = components["schemas"]["SearchIndexStatus"];
type Texts = Dictionary["admin"];

/**
 * The search index's size and queue, the external engine's state (when one is configured), and a button that queues a
 * full rebuild (done in the background).
 */
export function SearchIndexPanel({ locale, texts, status }: { locale: string; texts: Texts; status: Status }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm(texts.errors as ErrorTexts);
  const [queued, setQueued] = useState(false);

  async function rebuild() {
    const result = await submit("/api/v1/admin/seo/search-index/rebuild", undefined);
    if (!result.ok) return;
    setQueued(true);
    router.refresh();
  }

  return (
    <div className="flex flex-col gap-3 text-sm">
      <p className="text-muted">{texts.searchIndexIntro}</p>
      <p>{texts.searchIndexStatus.replace("{documents}", formatNumber(status.documents, locale)).replace("{pending}", formatNumber(status.pending, locale))}</p>
      {status.engine && (
        <p className={status.engine.serving ? undefined : "text-danger"}>
          {(status.engine.serving ? texts.searchEngineServing : texts.searchEngineFallback)
            .replace("{engine}", status.engine.provider)
            .replace("{pending}", formatNumber(status.engine.pending, locale))}
        </p>
      )}
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {queued && <FormAlert kind="success">{texts.searchIndexQueued}</FormAlert>}
      <div>
        <Button variant="secondary" onClick={rebuild} disabled={busy}>
          {texts.searchIndexRebuild}
        </Button>
      </div>
    </div>
  );
}
