"use client";

import Link from "next/link";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { Icon } from "@/components/ui/Icon";
import { sendJson } from "@/lib/account/client";
import { problemMessage, type ErrorTexts } from "@/lib/account/problems";
import { notifyCartChanged } from "@/lib/cart/count";

export type AddToCartTexts = { addToCart: string; added: string; viewCart: string; outOfStock: string };

type Props = {
  locale: string;
  number: number;
  /** The chosen variant, for products with variants. */
  variantSku?: string | null;
  inStock: boolean;
  texts: AddToCartTexts;
  errors: ErrorTexts;
};

/**
 * Adds one piece to the cart (the API checks the price and stock). Then says so with a link to the cart; refused adds
 * (sold out meanwhile, not enough stock) say why.
 */
export function AddToCartButton({ locale, number, variantSku = null, inStock, texts, errors }: Props) {
  const [busy, setBusy] = useState(false);
  const [state, setState] = useState<{ added: boolean; error: string | null }>({ added: false, error: null });

  async function add() {
    setBusy(true);
    const result = await sendJson("POST", `/api/v1/cart/items?locale=${locale}`, { number, variantSku, quantity: 1 });
    setBusy(false);
    if (!result.ok) {
      setState({ added: false, error: problemMessage(result.problem, errors) });
      return;
    }
    setState({ added: true, error: null });
    notifyCartChanged();
  }

  return (
    <div className="flex flex-col gap-2">
      <Button size="lg" onClick={add} disabled={busy || !inStock} className="gap-2">
        <Icon name="cart" size={20} />
        {inStock ? texts.addToCart : texts.outOfStock}
      </Button>
      <p role="status" className="min-h-5 text-sm">
        {state.added && (
          <>
            <span className="text-success">{texts.added}</span>{" "}
            <Link href={`/${locale}/cart`} className="font-medium text-primary underline-offset-2 hover:underline">
              {texts.viewCart}
            </Link>
          </>
        )}
        {state.error && <span className="text-danger">{state.error}</span>}
      </p>
    </div>
  );
}
