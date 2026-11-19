"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import type { ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";

type SpecChoice = components["schemas"]["SpecChoice"];
type Texts = Dictionary["admin"];

type Props = {
  texts: Texts;
  /** PUT endpoint of this category's or sub-category's template. */
  path: `/api/v1/${string}`;
  specs: SpecChoice[];
  selected: string[];
  /** A sub-category's view: the category's specs, shown ticked and fixed (they apply already). */
  inherited?: string[];
};

/**
 * The spec template of a category or sub-category: the specs its products usually have. The product editor lists
 * them first (a sub-category adds to its category's).
 */
export function TemplatePicker({ texts, path, specs, selected, inherited = [] }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.catalogErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const form = new FormData(event.currentTarget);
    const specCodes = specs.map((s) => s.code).filter((code) => form.get(`template-${code}`) === "on" && !inherited.includes(code));
    if ((await submit(path, { specCodes }, "PUT")).ok) {
      setSaved(true);
      router.refresh();
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-2 rounded-xl border border-border p-3">
      <p className="text-sm font-bold">{texts.templateTitle}</p>
      <p className="text-xs text-muted">{inherited.length > 0 ? texts.templateHintSub : texts.templateHint}</p>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && <FormAlert kind="success">{texts.saved}</FormAlert>}
      {specs.length === 0 ? (
        <p className="text-sm text-muted">{texts.specsEmpty}</p>
      ) : (
        <div className="grid gap-1 sm:grid-cols-2 lg:grid-cols-3">
          {specs.map((spec) => {
            const fixed = inherited.includes(spec.code);
            return (
              <label key={spec.code} className={`flex min-h-9 items-center gap-2 text-sm ${fixed ? "text-muted" : ""}`}>
                <input
                  type="checkbox"
                  name={`template-${spec.code}`}
                  defaultChecked={fixed || selected.includes(spec.code)}
                  disabled={fixed}
                  className="h-4 w-4"
                />
                {spec.groupName ? `${spec.groupName} › ${spec.name}` : spec.name}
              </label>
            );
          })}
        </div>
      )}
      <div>
        <SubmitButton busy={busy} label={texts.save} busyLabel="…" />
      </div>
    </form>
  );
}
