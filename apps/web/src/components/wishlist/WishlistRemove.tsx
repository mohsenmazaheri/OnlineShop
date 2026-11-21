"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { sendJson } from "@/lib/account/client";
import { Icon } from "@/components/ui/Icon";

/** Takes a product off the wishlist and reloads the list. */
export function WishlistRemove({ number, label }: { number: number; label: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function remove() {
    setBusy(true);
    const result = await sendJson("DELETE", `/api/v1/account/wishlist/${number}`);
    setBusy(false);
    if (result.ok) router.refresh();
  }

  return (
    <button
      type="button"
      onClick={remove}
      disabled={busy}
      aria-label={label}
      title={label}
      className="flex h-10 w-full items-center justify-center gap-2 rounded-xl border border-border text-sm text-muted hover:border-danger hover:text-danger"
    >
      <Icon name="close" size={16} />
      <span aria-hidden>{label}</span>
    </button>
  );
}
