"use client";

import { useRouter } from "next/navigation";
import { useState, type ChangeEvent } from "react";
import { FormAlert } from "@/components/account/FormParts";
import { Button, buttonClass } from "@/components/ui/Button";
import { sendJson, uploadFile } from "@/lib/account/client";
import { messageFor, problemMessage, type ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";

type ImportReport = components["schemas"]["ImportReport"];
type Texts = Dictionary["admin"];

type Props = { locale: string; texts: Texts; number: number };

/** The count sheet to download, counts from a filled-in sheet, and posting or cancelling the stocktake. */
export function StocktakeActions({ locale, texts, number }: Props) {
  const router = useRouter();
  const errorTexts = { ...texts.errors, ...texts.inventoryErrors, ...texts.importErrors } as ErrorTexts;
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<ImportReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const base = `/api/v1/admin/inventory/stocktakes/${number}` as const;

  function choose(event: ChangeEvent<HTMLInputElement>) {
    setFile(event.target.files?.[0] ?? null);
    setReport(null);
    setError(null);
  }

  async function importCounts() {
    if (!file) return;
    setBusy(true);
    setError(null);
    const result = await uploadFile<ImportReport>(`${base}/counts/import`, file);
    setBusy(false);
    if (!result.ok) {
      setError(problemMessage(result.problem, errorTexts));
      return;
    }
    setReport(result.data);
    if (result.data.applied) router.refresh();
  }

  async function close(action: "post" | "cancel") {
    if (!window.confirm(action === "post" ? texts.stocktakeConfirmPost : texts.stocktakeConfirmCancel)) return;
    setBusy(true);
    setError(null);
    const result = await sendJson("POST", `${base}/${action}`);
    setBusy(false);
    if (!result.ok) setError(problemMessage(result.problem, errorTexts));
    else router.refresh();
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium">{texts.stocktakeSheet}</span>
        <a href={`${base}/sheet?format=xlsx&locale=${locale}`} download className={buttonClass("secondary", "sm")}>
          Excel (.xlsx)
        </a>
        <a href={`${base}/sheet?format=csv&locale=${locale}`} download className={buttonClass("secondary", "sm")}>
          CSV
        </a>
      </div>

      <div className="flex flex-wrap items-end gap-2">
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium">{texts.stocktakeImport}</span>
          <input type="file" accept=".xlsx,.csv,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={choose} className="text-sm" />
        </label>
        <Button variant="secondary" onClick={importCounts} disabled={!file || busy}>
          {texts.stocktakeImportSend}
        </Button>
      </div>

      <div aria-live="polite" className="flex flex-col gap-2">
        {error && <FormAlert kind="error">{error}</FormAlert>}
        {report?.applied && <FormAlert kind="success">{texts.stocktakeImportDone.replace("{count}", formatNumber(report.updated, locale))}</FormAlert>}
        {report && report.problems.length > 0 && (
          <>
            <FormAlert kind="error">{texts.importProblems.replace("{count}", String(report.problems.length))}</FormAlert>
            <ul className="max-h-60 overflow-auto text-sm">
              {report.problems.map((p, i) => (
                <li key={i}>
                  {texts.importRow} {formatNumber(p.row, locale)}
                  {p.column && (
                    <span dir="ltr">
                      {" "}
                      ({p.column})
                    </span>
                  )}
                  : {messageFor(p.code, errorTexts)}
                </li>
              ))}
            </ul>
          </>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-2 border-t border-border pt-4">
        <Button onClick={() => close("post")} disabled={busy}>
          {texts.stocktakePost}
        </Button>
        <Button variant="danger" onClick={() => close("cancel")} disabled={busy}>
          {texts.stocktakeCancel}
        </Button>
        <span className="text-xs text-muted">{texts.stocktakeUncountedNote}</span>
      </div>
    </div>
  );
}
