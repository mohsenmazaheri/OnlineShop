"use client";

import { useSyncExternalStore } from "react";
import { currentCount, subscribeCount } from "@/lib/cart/count";

/**
 * The number of pieces in the cart on the cart icon (nothing when empty). Read from a cookie, so it costs no request;
 * the server renders nothing (the page stays the same for every visitor).
 */
export function CartBadge({ locale, label }: { locale: string; label: string }) {
  const count = useSyncExternalStore(subscribeCount, currentCount, () => 0);
  if (count === 0) return null;
  const text = new Intl.NumberFormat(locale).format(Math.min(count, 99));
  return (
    <span className="absolute -end-1 -top-1 grid h-5 min-w-5 place-items-center rounded-full bg-danger px-1 text-[11px] font-bold text-danger-foreground">
      <span aria-hidden>{count > 99 ? `${text}+` : text}</span>
      <span className="sr-only">{label.replace("{count}", String(count))}</span>
    </span>
  );
}
