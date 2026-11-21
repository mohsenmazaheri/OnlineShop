"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";

type SpecList = components["schemas"]["SpecList"];
type SpecView = components["schemas"]["SpecView"];
type SpecText = components["schemas"]["SpecText"];
type OptionText = components["schemas"]["OptionText"];
type Texts = Dictionary["admin"];

export const specKinds = ["Text", "Number", "Boolean", "Option"] as const;

type OptionDraft = { key: string; code: string | null; texts: OptionText[]; usageCount: number };
export const specBetters = ["None", "Higher", "Lower"] as const;
type Better = (typeof specBetters)[number];

type SpecDraft = {
  code: string;
  kind: string;
  isFilterable: boolean;
  isComparable: boolean;
  showInSummary: boolean;
  better: Better;
  texts: SpecText[];
  options: OptionDraft[];
};

/** An editable copy of a spec with a text entry for every shop language (empty where not translated). */
export function toDraft(spec: SpecView | undefined, locales: string[]): SpecDraft {
  const text = (l: string): SpecText => spec?.texts.find((t) => t.locale === l) ?? { locale: l, name: "", groupName: null, unit: null };
  return {
    code: spec?.code ?? "",
    kind: spec?.kind ?? "Text",
    isFilterable: spec?.isFilterable ?? false,
    isComparable: spec?.isComparable ?? true,
    showInSummary: spec?.showInSummary ?? false,
    better: spec?.better ?? "None",
    texts: locales.map(text),
    options: (spec?.options ?? []).map((o) => ({
      key: o.code,
      code: o.code,
      usageCount: o.usageCount,
      texts: locales.map((l) => o.texts.find((t) => t.locale === l) ?? { locale: l, value: "" }),
    })),
  };
}

/** The request body for a draft: options only for the option kind; empty optional texts become null. */
export function toRequest(draft: SpecDraft) {
  return {
    code: draft.code.trim() || null,
    kind: draft.kind,
    isFilterable: draft.isFilterable,
    isComparable: draft.isComparable,
    showInSummary: draft.showInSummary,
    // Only numbers have a better direction (the API ignores it for other kinds too).
    better: draft.kind === "Number" ? draft.better : "None",
    texts: draft.texts.map((t) => ({ locale: t.locale, name: t.name.trim(), groupName: t.groupName?.trim() || null, unit: t.unit?.trim() || null })),
    options: draft.kind === "Option" ? draft.options.map((o) => ({ code: o.code, texts: o.texts.map((t) => ({ locale: t.locale, value: t.value.trim() })) })) : [],
  };
}

/** The name in the admin's language, else the default language. */
export function specName(texts: SpecText[], locale: string): string {
  return texts.find((t) => t.locale === locale && t.name)?.name ?? texts[0]?.name ?? "";
}

type Shared = { locale: string; texts: Texts; list: SpecList; languageNames: Record<string, string> };

let newKeys = 0;

function SpecForm({ spec, ...shared }: Shared & { spec?: SpecView }) {
  const { locale, texts, list, languageNames } = shared;
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.catalogErrors } as ErrorTexts);
  const [draft, setDraft] = useState(() => toDraft(spec, list.locales));
  const [saved, setSaved] = useState(false);
  const used = (spec?.usageCount ?? 0) > 0;
  const input = "h-10 w-full rounded-xl border border-border bg-surface px-3 text-sm";

  const setText = (i: number, patch: Partial<SpecText>) => setDraft({ ...draft, texts: draft.texts.map((t, j) => (j === i ? { ...t, ...patch } : t)) });
  const setOption = (i: number, locale: string, value: string) =>
    setDraft({
      ...draft,
      options: draft.options.map((o, j) => (j === i ? { ...o, texts: o.texts.map((t) => (t.locale === locale ? { ...t, value } : t)) } : o)),
    });

  async function done() {
    await refreshStorefront(locale, ["catalog"]);
    router.refresh();
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const result = await submit<SpecView>(spec ? `/api/v1/admin/specs/${spec.id}` : "/api/v1/admin/specs", toRequest(draft), spec ? "PUT" : "POST");
    if (!result.ok) return;
    setSaved(true);
    setDraft(spec ? toDraft(result.data, list.locales) : toDraft(undefined, list.locales));
    await done();
  }

  async function remove() {
    if (!spec || !window.confirm(texts.confirmDeleteSpec)) return;
    if ((await submit(`/api/v1/admin/specs/${spec.id}`, undefined, "DELETE")).ok) await done();
  }

  async function move(offset: -1 | 1) {
    if (!spec) return;
    const ids = list.specs.map((s) => s.id);
    const i = ids.indexOf(spec.id);
    const j = i + offset;
    if (j < 0 || j >= ids.length) return;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    if ((await submit("/api/v1/admin/specs/order", { ids }, "PUT")).ok) await done();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && spec && <FormAlert kind="success">{texts.saved}</FormAlert>}

      <div className="grid gap-3 sm:grid-cols-2">
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium">{texts.specCode}</span>
          <input value={draft.code} onChange={(e) => setDraft({ ...draft, code: e.target.value })} placeholder={texts.specCodeHint} maxLength={64} dir="ltr" className={input} />
        </label>
        <div className="flex flex-col gap-1 text-sm">
          <label className="flex flex-col gap-1">
            <span className="font-medium">{texts.specKind}</span>
            <select
              value={draft.kind}
              disabled={used}
              aria-describedby={used ? `kind-fixed-${spec?.id}` : undefined}
              onChange={(e) => setDraft({ ...draft, kind: e.target.value })}
              className={input}
            >
              {specKinds.map((k) => (
                <option key={k} value={k}>
                  {texts.specKinds[k]}
                </option>
              ))}
            </select>
          </label>
          {used && (
            <span id={`kind-fixed-${spec?.id}`} className="text-xs text-muted">
              {texts.specKindFixed}
            </span>
          )}
        </div>
      </div>
      <div className="flex flex-wrap gap-4 text-sm">
        {(["isFilterable", "isComparable", "showInSummary"] as const).map((flag) => (
          <label key={flag} className="flex min-h-10 items-center gap-2">
            <input type="checkbox" checked={draft[flag]} onChange={(e) => setDraft({ ...draft, [flag]: e.target.checked })} className="h-4 w-4" />
            {texts.specFlags[flag]}
          </label>
        ))}
      </div>
      {draft.kind === "Number" && draft.isComparable && (
        <label className="flex flex-col gap-1 text-sm sm:max-w-xs">
          <span>{texts.specBetter}</span>
          <select value={draft.better} onChange={(e) => setDraft({ ...draft, better: e.target.value as Better })} className={input}>
            {specBetters.map((b) => (
              <option key={b} value={b}>
                {texts.specBetters[b]}
              </option>
            ))}
          </select>
        </label>
      )}

      {draft.texts.map((t, i) => (
        <fieldset key={t.locale} lang={t.locale} className="grid gap-2 rounded-xl border border-border p-3 sm:grid-cols-3">
          <legend className="flex items-center gap-2 px-1 text-sm font-bold">
            {languageNames[t.locale] ?? t.locale}
            {i > 0 && !t.name && <span className="rounded-md bg-accent px-1.5 text-xs font-normal text-accent-foreground">{texts.untranslated}</span>}
          </legend>
          <label className="flex flex-col gap-1 text-sm">
            <span>{texts.nameLabel}</span>
            <input value={t.name} onChange={(e) => setText(i, { name: e.target.value })} required={i === 0} maxLength={200} className={input} />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span>{texts.specGroup}</span>
            <input value={t.groupName ?? ""} onChange={(e) => setText(i, { groupName: e.target.value })} maxLength={200} className={input} />
          </label>
          {draft.kind === "Number" && (
            <label className="flex flex-col gap-1 text-sm">
              <span>{texts.specUnit}</span>
              <input value={t.unit ?? ""} onChange={(e) => setText(i, { unit: e.target.value })} maxLength={32} className={input} />
            </label>
          )}
        </fieldset>
      ))}

      {draft.kind === "Option" && (
        <fieldset className="flex flex-col gap-2 rounded-xl border border-border p-3">
          <legend className="px-1 text-sm font-bold">{texts.specOptions}</legend>
          {draft.options.length === 0 && <p className="text-sm text-muted">{texts.specNoOptions}</p>}
          {draft.options.map((option, i) => (
            <div key={option.key} className="flex flex-wrap items-end gap-2 border-b border-border pb-2 last:border-0">
              {option.texts.map((t) => (
                <label key={t.locale} lang={t.locale} className="flex min-w-28 flex-1 flex-col gap-1 text-xs">
                  <span className="text-muted">{languageNames[t.locale] ?? t.locale}</span>
                  <input value={t.value} onChange={(e) => setOption(i, t.locale, e.target.value)} maxLength={200} className={input} />
                </label>
              ))}
              {option.code && (
                <span className="pb-2 text-xs text-muted" dir="ltr">
                  {option.code}
                </span>
              )}
              <Button
                variant="ghost"
                size="sm"
                disabled={option.usageCount > 0}
                title={option.usageCount > 0 ? texts.optionInUse : undefined}
                onClick={() => setDraft({ ...draft, options: draft.options.filter((_, j) => j !== i) })}
              >
                {texts.remove}
              </Button>
            </div>
          ))}
          <Button
            variant="secondary"
            size="sm"
            onClick={() =>
              setDraft({ ...draft, options: [...draft.options, { key: `new-${++newKeys}`, code: null, usageCount: 0, texts: list.locales.map((l) => ({ locale: l, value: "" })) }] })
            }
          >
            + {texts.specAddOption}
          </Button>
        </fieldset>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <SubmitButton busy={busy} label={spec ? texts.save : texts.add} busyLabel="…" />
        {spec && (
          <>
            <Button variant="danger" onClick={remove} disabled={busy || used} title={used ? texts.specInUse : undefined}>
              {texts.remove}
            </Button>
            <Button variant="ghost" size="sm" onClick={() => move(-1)} disabled={busy}>
              {texts.imageMoveUp}
            </Button>
            <Button variant="ghost" size="sm" onClick={() => move(1)} disabled={busy}>
              {texts.imageMoveDown}
            </Button>
          </>
        )}
      </div>
    </form>
  );
}

/** The spec library: each spec opens to its form (kind, flags, texts per language, options); plus "add a spec". */
export function SpecsEditor(shared: Shared) {
  const { locale, texts, list } = shared;
  return (
    <div className="flex flex-col gap-3">
      {list.specs.length === 0 && <p className="text-sm text-muted">{texts.specsEmpty}</p>}
      {list.specs.map((spec) => (
        <details key={spec.id} className="rounded-xl border border-border bg-surface p-3 open:shadow-sm">
          <summary className="flex cursor-pointer flex-wrap items-center justify-between gap-2 font-medium">
            <span>
              {specName(spec.texts, locale)}
              {spec.texts.find((t) => t.locale === locale)?.groupName && (
                <span className="text-sm font-normal text-muted"> · {spec.texts.find((t) => t.locale === locale)?.groupName}</span>
              )}
            </span>
            <span className="text-xs text-muted">
              {texts.specKinds[spec.kind as (typeof specKinds)[number]]} · {texts.specUsage.replace("{count}", String(spec.usageCount))}
            </span>
          </summary>
          <div className="mt-3">
            <SpecForm {...shared} spec={spec} />
          </div>
        </details>
      ))}
      <details className="rounded-xl border border-dashed border-border bg-surface p-3">
        <summary className="cursor-pointer font-medium">+ {texts.addSpec}</summary>
        <div className="mt-3">
          <SpecForm {...shared} />
        </div>
      </details>
    </div>
  );
}
