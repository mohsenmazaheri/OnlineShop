"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { sendJson } from "@/lib/account/client";
import { buttonClass } from "@/components/ui/Button";
import { Icon } from "@/components/ui/Icon";
import { useHydrated } from "@/components/compare/useCompare";

type Ids = { numbers: number[]; signedIn: boolean };

let loading: Promise<Ids> | null = null;

/** The visitor's wishlist (product numbers), asked once per page. */
function wishlistIds(): Promise<Ids> {
  loading ??= fetch("/api/v1/account/wishlist/ids", { credentials: "same-origin", headers: { Accept: "application/json" } })
    .then((r) => (r.ok ? (r.json() as Promise<Ids>) : { numbers: [], signedIn: false }))
    .catch(() => ({ numbers: [], signedIn: false }));
  return loading;
}

/** For tests: forget the page's answer. */
export function resetWishlistIds() {
  loading = null;
}

type Props = {
  locale: string;
  number: number;
  /** Where to come back to after signing in. */
  returnPath: string;
  texts: { wishlistAdd: string; wishlistAdded: string };
};

/**
 * The heart on the product page: adds the product to the signed-in customer's wishlist or takes it off. A guest is
 * sent to sign in and back. Without JavaScript it is a link to the wishlist page (sign-in first when needed).
 */
export function WishlistButton({ locale, number, returnPath, texts }: Props) {
  const router = useRouter();
  const hydrated = useHydrated();
  const [ids, setIds] = useState<Ids | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let live = true;
    void wishlistIds().then((answer) => live && setIds(answer));
    return () => {
      live = false;
    };
  }, []);

  const active = ids?.numbers.includes(number) ?? false;
  const label = active ? texts.wishlistAdded : texts.wishlistAdd;
  const className = buttonClass("secondary", "md", active ? "gap-2 border-danger text-danger" : "gap-2");
  const content = (
    <>
      <Icon name="heart" size={20} fill={active ? "currentColor" : "none"} />
      <span>{label}</span>
    </>
  );

  if (!hydrated)
    return (
      <a href={`/${locale}/account/wishlist`} className={className} rel="nofollow">
        {content}
      </a>
    );

  async function toggle() {
    const answer = await wishlistIds();
    if (!answer.signedIn) {
      router.push(`/${locale}/account/login?returnUrl=${encodeURIComponent(returnPath)}`);
      return;
    }
    setBusy(true);
    const result = await sendJson<Ids>(active ? "DELETE" : "PUT", `/api/v1/account/wishlist/${number}`);
    setBusy(false);
    if (!result.ok) return;
    const next = { numbers: result.data.numbers, signedIn: true };
    loading = Promise.resolve(next);
    setIds(next);
  }

  return (
    <button type="button" className={className} aria-pressed={active} disabled={busy || ids === null} onClick={toggle}>
      {content}
    </button>
  );
}
