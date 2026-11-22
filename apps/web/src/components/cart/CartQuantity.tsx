"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Icon } from "@/components/ui/Icon";
import { sendJson } from "@/lib/account/client";
import { problemMessage, type ErrorTexts } from "@/lib/account/problems";
import { notifyCartChanged } from "@/lib/cart/count";

type Texts = { decrease: string; increase: string; remove: string; quantity: string };

type Props = { lineId: string; quantity: number; max: number; name: string; texts: Texts; errors: ErrorTexts };

/** − quantity + and remove for one cart line; each change goes to the API, then the page reloads its prices. */
export function CartQuantity({ lineId, quantity, max, name, texts, errors }: Props) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function change(next: number) {
    setBusy(true);
    setError(null);
    const result = next <= 0 ? await sendJson("DELETE", `/api/v1/cart/items/${lineId}`) : await sendJson("PUT", `/api/v1/cart/items/${lineId}`, { quantity: next });
    setBusy(false);
    if (!result.ok) {
      setError(problemMessage(result.problem, errors));
      return;
    }
    notifyCartChanged();
    router.refresh();
  }

  const button = "grid h-9 w-9 place-items-center rounded-lg border border-border hover:border-primary disabled:opacity-50";
  return (
    <div className="flex flex-col items-start gap-1">
      <div className="flex items-center gap-2">
        <div role="group" aria-label={`${texts.quantity}: ${name}`} className="flex items-center gap-1">
          <button type="button" className={button} onClick={() => change(quantity - 1)} disabled={busy} aria-label={texts.decrease}>
            −
          </button>
          <span className="min-w-8 text-center font-bold tabular-nums" aria-live="polite">
            {quantity}
          </span>
          <button type="button" className={button} onClick={() => change(quantity + 1)} disabled={busy || quantity >= max} aria-label={texts.increase}>
            +
          </button>
        </div>
        <button
          type="button"
          onClick={() => change(0)}
          disabled={busy}
          aria-label={texts.remove.replace("{name}", name)}
          title={texts.remove.replace("{name}", name)}
          className="grid h-9 w-9 place-items-center rounded-lg text-muted hover:text-danger"
        >
          <Icon name="close" size={16} />
        </button>
      </div>
      {error && (
        <p role="alert" className="text-xs text-danger">
          {error}
        </p>
      )}
    </div>
  );
}
