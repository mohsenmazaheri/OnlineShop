"use client";

import { useRouter } from "next/navigation";
import { useState, type ChangeEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { Button, buttonClass } from "@/components/ui/Button";
import { uploadFile } from "@/lib/account/client";
import { messageFor, problemMessage, type ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";

type ImportReport = components["schemas"]["ImportReport"];
type Texts = Dictionary["admin"];
export type TransferKind = "products" | "stock";

/** Where a sheet of this kind is checked (dry run) or imported. */
export function importPath(kind: TransferKind, check: boolean): `/api/v1/${string}` {
  return `/api/v1/admin/catalog/import/${kind}?dryRun=${check}`;
}

/** Download links of a sheet of this kind. */
export function exportPath(kind: TransferKind, format: "xlsx" | "csv"): string {
  return `/api/v1/admin/catalog/export/${kind}?format=${format}`;
}

type Props = { locale: string; kind: TransferKind; texts: Texts };

/**
 * Download a sheet, edit it in Excel (or any spreadsheet program), check it, then import it. Import is offered only
 * after a check of the same file found no problems; the server saves nothing unless every row is valid.
 */
export function ProductTransfer({ locale, kind, texts }: Props) {
  const router = useRouter();
  const errorTexts = { ...texts.errors, ...texts.catalogErrors, ...texts.importErrors } as ErrorTexts;
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<ImportReport | null>(null);
  const [checked, setChecked] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function choose(event: ChangeEvent<HTMLInputElement>) {
    setFile(event.target.files?.[0] ?? null);
    setReport(null);
    setChecked(false);
    setError(null);
  }

  async function send(check: boolean) {
    if (!file) return;
    setBusy(true);
    setError(null);
    const result = await uploadFile<ImportReport>(importPath(kind, check), file);
    setBusy(false);
    if (!result.ok) {
      setError(problemMessage(result.problem, errorTexts));
      setReport(null);
      return;
    }
    setReport(result.data);
    setChecked(check && result.data.problems.length === 0);
    if (result.data.applied) {
      await refreshStorefront(locale, ["catalog"]);
      router.refresh();
    }
  }

  const summary = report
    ? texts.importSummary
        .replace("{rows}", String(report.rows))
        .replace("{created}", String(report.created))
        .replace("{updated}", String(report.updated))
    : null;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium">{texts.exportDownload}</span>
        <a href={exportPath(kind, "xlsx")} download className={buttonClass("secondary", "sm")}>
          Excel (.xlsx)
        </a>
        <a href={exportPath(kind, "csv")} download className={buttonClass("secondary", "sm")}>
          CSV
        </a>
      </div>

      <div className="flex flex-col gap-2">
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium">{texts.importFile}</span>
          <input type="file" accept=".xlsx,.csv,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={choose} className="text-sm" />
        </label>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => send(true)} disabled={!file || busy}>
            {texts.importCheck}
          </Button>
          <Button onClick={() => send(false)} disabled={!file || busy || !checked}>
            {texts.importApply}
          </Button>
        </div>
        {!checked && file && !report && <p className="text-xs text-muted">{texts.importCheckFirst}</p>}
      </div>

      <div aria-live="polite" className="flex flex-col gap-2">
        {error && <FormAlert kind="error">{error}</FormAlert>}
        {report?.applied && <FormAlert kind="success">{`${texts.importDone} ${summary}`}</FormAlert>}
        {report && !report.applied && report.problems.length === 0 && <FormAlert kind="success">{`${texts.importReady} ${summary}`}</FormAlert>}
        {report && report.problems.length > 0 && (
          <>
            <FormAlert kind="error">{texts.importProblems.replace("{count}", String(report.problems.length))}</FormAlert>
            <div className="max-h-80 overflow-auto">
              <table className="w-full text-sm">
                <thead className="text-xs text-muted">
                  <tr>
                    <th className="p-1 text-start font-medium">{texts.importRow}</th>
                    <th className="p-1 text-start font-medium">{texts.importColumn}</th>
                    <th className="p-1 text-start font-medium">{texts.importProblem}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.problems.map((p, i) => (
                    <tr key={i} className="border-t border-border">
                      <td className="p-1">{p.row.toLocaleString(locale)}</td>
                      <td className="p-1" dir="ltr">
                        {p.column ?? "—"}
                      </td>
                      <td className="p-1">{messageFor(p.code, errorTexts)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
