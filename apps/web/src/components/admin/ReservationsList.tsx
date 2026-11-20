"use client";

import { useRouter } from "next/navigation";
import { FormAlert } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatNumber } from "@/lib/i18n/format-money";

type ActiveReservation = components["schemas"]["ActiveReservationView"];
type Texts = Dictionary["admin"];

type Props = { locale: string; texts: Texts; reservations: ActiveReservation[]; canManage: boolean };

/** Units held for unpaid checkouts, with "release" per checkout reference (inventory.manage). */
export function ReservationsList({ locale, texts, reservations, canManage }: Props) {
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.inventoryErrors } as ErrorTexts);

  async function release(reference: string) {
    if (!window.confirm(texts.reservationConfirmRelease.replace("{reference}", reference))) return;
    const result = await submit("/api/v1/admin/inventory/reservations/release", { reference });
    if (result.ok) router.refresh();
  }

  if (reservations.length === 0) return <p className="text-sm text-muted">{texts.reservationsNone}</p>;

  return (
    <div className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      <div className="overflow-x-auto rounded-2xl border border-border bg-surface">
        <table className="w-full min-w-[36rem] text-sm">
          <thead className="bg-surface-muted text-xs text-muted">
            <tr>
              <th scope="col" className="p-3 text-start font-medium">{texts.stockItem}</th>
              <th scope="col" className="p-3 text-end font-medium">{texts.movementQuantity}</th>
              <th scope="col" className="p-3 text-start font-medium">{texts.reservationReference}</th>
              <th scope="col" className="p-3 text-start font-medium">{texts.reservationExpires}</th>
              {canManage && (
                <th scope="col" className="p-3">
                  <span className="sr-only">{texts.reservationRelease}</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {reservations.map((r) => (
              <tr key={r.id}>
                <th scope="row" className="p-3 text-start font-normal">
                  <span className="block">{r.name}</span>
                  {r.variantLabel && <span className="block text-xs">{r.variantLabel}</span>}
                  <span dir="ltr" className="block text-xs text-muted">
                    {r.sku}
                  </span>
                </th>
                <td className="p-3 text-end font-bold tabular-nums">{formatNumber(r.quantity, locale)}</td>
                <td className="p-3" dir="ltr">
                  {r.reference}
                </td>
                <td className="whitespace-nowrap p-3 text-xs">{formatDate(r.expiresUtc, locale, true)}</td>
                {canManage && (
                  <td className="p-3 text-end">
                    <Button variant="secondary" size="sm" onClick={() => release(r.reference)} disabled={busy}>
                      {texts.reservationRelease}
                    </Button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
