"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";

type SynonymList = components["schemas"]["SynonymList"];
type SynonymView = components["schemas"]["SynonymView"];
type Texts = Dictionary["admin"];

/** Words typed with commas (Latin or Persian) or one per line. */
export function splitTerms(text: string): string[] {
  return text
    .split(/[,،\n]/)
    .map((t) => t.trim())
    .filter(Boolean);
}

type Shared = { texts: Texts; list: SynonymList; languageNames: Record<string, string> };

function SynonymForm({ group, texts, list, languageNames }: Shared & { group?: SynonymView }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.seoErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);
  const input = "h-10 rounded-xl border border-border bg-surface px-3 text-sm";

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const element = event.currentTarget;
    const form = new FormData(element);
    const body = { locale: String(form.get("locale") ?? ""), terms: splitTerms(String(form.get("terms") ?? "")) };
    const result = await submit(group ? `/api/v1/admin/seo/synonyms/${group.id}` : "/api/v1/admin/seo/synonyms", body, group ? "PUT" : "POST");
    if (!result.ok) return;
    setSaved(true);
    if (!group) element.reset();
    router.refresh();
  }

  async function remove() {
    if (group && (await submit(`/api/v1/admin/seo/synonyms/${group.id}`, undefined, "DELETE")).ok) router.refresh();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-2 border-t border-border pt-3 first:border-0 first:pt-0">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && group && <FormAlert kind="success">{texts.saved}</FormAlert>}
      <div className="grid gap-2 sm:grid-cols-[10rem_1fr_auto_auto] sm:items-end">
        <label className="flex flex-col gap-1 text-sm">
          <span>{texts.language}</span>
          <select name="locale" defaultValue={group?.locale ?? list.locales[0]} className={input}>
            {list.locales.map((l) => (
              <option key={l} value={l}>
                {languageNames[l] ?? l}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>{texts.synonymTerms}</span>
          <input name="terms" defaultValue={group?.terms.join("، ") ?? ""} placeholder={texts.synonymTermsHint} className={input} />
        </label>
        <SubmitButton busy={busy} label={group ? texts.save : texts.add} busyLabel="…" />
        {group && (
          <Button variant="ghost" onClick={remove} disabled={busy}>
            {texts.remove}
          </Button>
        )}
      </div>
    </form>
  );
}

/** Search synonyms per language: each group is a line of words that find each other. */
export function SynonymsEditor(shared: Shared) {
  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-muted">{shared.texts.synonymsIntro}</p>
      {shared.list.groups.map((group) => (
        <SynonymForm key={group.id} {...shared} group={group} />
      ))}
      <div className="rounded-xl border border-dashed border-border p-3">
        <SynonymForm {...shared} />
      </div>
    </div>
  );
}
