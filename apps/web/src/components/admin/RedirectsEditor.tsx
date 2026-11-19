"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";

type RedirectView = components["schemas"]["RedirectView"];
type ImportResult = components["schemas"]["ImportRedirectsResult"];
type Texts = Dictionary["admin"];

export const redirectKinds = ["Permanent", "Temporary", "Gone"] as const;

type Props = { locale: string; texts: Texts; redirects: RedirectView[] };

function AddRedirect({ texts }: { texts: Texts }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.seoErrors } as ErrorTexts);
  const [kind, setKind] = useState<(typeof redirectKinds)[number]>("Permanent");
  const input = "h-10 w-full rounded-xl border border-border bg-surface px-3 text-sm";

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const body = { fromPath: String(form.get("fromPath") ?? "").trim(), toPath: String(form.get("toPath") ?? "").trim() || null, kind };
    if ((await submit("/api/v1/admin/seo/redirects", body, "POST")).ok) {
      element.reset();
      router.refresh();
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-2">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <div className="grid gap-2 sm:grid-cols-[1fr_1fr_10rem_auto] sm:items-end">
        <label className="flex flex-col gap-1 text-sm">
          <span>{texts.redirectFrom}</span>
          <input name="fromPath" placeholder="/old-page" dir="ltr" maxLength={500} className={input} />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>{texts.redirectTo}</span>
          <input name="toPath" placeholder="/fa/p/1001" dir="ltr" maxLength={2048} disabled={kind === "Gone"} className={input} />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>{texts.redirectKind}</span>
          <select value={kind} onChange={(e) => setKind(e.target.value as typeof kind)} className={input}>
            {redirectKinds.map((k) => (
              <option key={k} value={k}>
                {texts.redirectKinds[k]}
              </option>
            ))}
          </select>
        </label>
        <SubmitButton busy={busy} label={texts.add} busyLabel="…" />
      </div>
    </form>
  );
}

function ImportRedirects({ texts }: { texts: Texts }) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.seoErrors } as ErrorTexts);
  const [result, setResult] = useState<ImportResult | null>(null);
  // Codes come as "seo.path_invalid"; the texts are keyed without the prefix.
  const errorText = (code: string) => (texts.seoErrors as Record<string, string>)[code.replace(/^seo\./, "")] ?? code;

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setResult(null);
    const text = String(new FormData(event.currentTarget).get("csv") ?? "");
    const response = await submit<ImportResult>("/api/v1/admin/seo/redirects/import", { text }, "POST");
    if (!response.ok) return;
    setResult(response.data);
    router.refresh();
  }

  return (
    <details className="rounded-xl border border-dashed border-border p-3">
      <summary className="cursor-pointer text-sm font-medium">{texts.redirectImport}</summary>
      <form onSubmit={onSubmit} noValidate className="mt-3 flex flex-col gap-2">
        <p className="text-xs text-muted">{texts.redirectImportHint}</p>
        {formError && <FormAlert kind="error">{formError}</FormAlert>}
        {result && (
          <FormAlert kind={result.errors.length > 0 ? "error" : "success"}>
            {texts.redirectImported.replace("{added}", String(result.added)).replace("{updated}", String(result.updated))}
            {result.errors.length > 0 && (
              <ul className="mt-1 list-inside list-disc">
                {result.errors.map((e) => (
                  <li key={e.line}>{texts.redirectImportLine.replace("{line}", String(e.line)).replace("{error}", errorText(e.code))}</li>
                ))}
              </ul>
            )}
          </FormAlert>
        )}
        <textarea name="csv" rows={5} dir="ltr" placeholder={"/old-page,/fa/p/1001\n/old-category,/fa,302\n/removed,,410"} className="rounded-xl border border-border bg-surface px-3 py-2 font-mono text-sm" />
        <div>
          <SubmitButton busy={busy} label={texts.redirectImportButton} busyLabel="…" />
        </div>
      </form>
    </details>
  );
}

/** Redirects of old addresses: the list with usage counts, adding one, and importing many from CSV. */
export function RedirectsEditor({ locale, texts, redirects }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.seoErrors } as ErrorTexts);

  async function remove(id: string) {
    if (window.confirm(texts.confirmDeleteRedirect) && (await submit(`/api/v1/admin/seo/redirects/${id}`, undefined, "DELETE")).ok) router.refresh();
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted">{texts.redirectsIntro}</p>
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {redirects.length === 0 ? (
        <p className="text-sm text-muted">{texts.redirectsEmpty}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[36rem] text-sm">
            <thead className="text-xs text-muted">
              <tr>
                <th className="p-1 text-start font-medium">{texts.redirectFrom}</th>
                <th className="p-1 text-start font-medium">{texts.redirectTo}</th>
                <th className="p-1 text-start font-medium">{texts.redirectKind}</th>
                <th className="p-1 text-start font-medium">{texts.redirectHits}</th>
                <th className="p-1" />
              </tr>
            </thead>
            <tbody>
              {redirects.map((r) => (
                <tr key={r.id} className="border-t border-border">
                  <td className="p-1 break-all" dir="ltr">
                    {r.fromPath}
                  </td>
                  <td className="p-1 break-all" dir="ltr">
                    {r.toPath ?? "—"}
                  </td>
                  <td className="p-1">{texts.redirectKinds[r.kind]}</td>
                  <td className="p-1">
                    {r.hitCount.toLocaleString(locale)}
                    {r.lastHitUtc && <span className="block text-xs text-muted">{formatDate(r.lastHitUtc, locale)}</span>}
                  </td>
                  <td className="p-1">
                    <Button variant="ghost" size="sm" onClick={() => remove(r.id)} disabled={busy}>
                      {texts.remove}
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <AddRedirect texts={texts} />
      <ImportRedirects texts={texts} />
    </div>
  );
}
