import Image from "next/image";
import Link from "next/link";
import { displayMoney } from "@/components/product/Price";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Icon } from "@/components/ui/Icon";
import type { ErrorTexts } from "@/lib/account/problems";
import type { CartLine, CartView } from "@/lib/cart/server";
import { productPath } from "@/lib/catalog/product-path";
import { convertAmount, type DisplayCurrency } from "@/lib/currency/display";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatMoney, formatNumber } from "@/lib/i18n/format-money";
import { imageVariant } from "@/lib/media/image";
import { CartCountSync } from "./CartCountSync";
import { CartQuantity } from "./CartQuantity";

type Texts = Dictionary["cart"];

type Props = {
  cart: CartView;
  locale: string;
  money: DisplayCurrency;
  /** The "approximate price" note when another currency than the base is shown (null for the base). */
  approximate: string | null;
  texts: Texts;
  errors: ErrorTexts;
};

/** What is wrong with a line now (price and stock are live, so a line can change after it was added). */
function problemText(line: CartLine, texts: Texts, locale: string): string | null {
  switch (line.problem) {
    case "Unavailable":
      return texts.problemUnavailable;
    case "OutOfStock":
      return texts.problemOutOfStock;
    case "NotEnoughStock":
      return texts.problemNotEnoughStock.replace("{count}", formatNumber(line.available, locale));
    default:
      return null;
  }
}

/**
 * A line amount in the display currency: the converted unit price times the quantity, so "2 × 180.06" shows 360.12 and
 * the total is the sum of the shown lines (converting each total separately could round differently).
 */
export function shownAmount(unitPrice: number, quantity: number, money: DisplayCurrency): number {
  return Number((convertAmount(unitPrice, money) * quantity).toFixed(Math.max(money.decimals, 2)));
}

/**
 * The cart: one row per line (image, name, variant, price, quantity) and the summary. The quantity controls are the only
 * client part; every price comes from the API answer, converted for display only.
 */
export function CartContents({ cart, locale, money, approximate, texts, errors }: Props) {
  const format = (amount: number) => formatMoney(amount, money.code, locale, { symbol: money.symbol, decimals: money.decimals });
  // Lines with a problem are not counted, as in the API's subtotal.
  const total = cart.lines.filter((l) => l.problem === "None").reduce((sum, l) => sum + shownAmount(l.unitPrice, l.quantity, money), 0);
  if (cart.lines.length === 0) {
    return (
      <div className="flex flex-col items-center gap-4 py-10 text-center">
        <CartCountSync count={0} />
        <span className="tint tint-0 flex h-20 w-20 items-center justify-center rounded-3xl">
          <Icon name="cart" size={36} />
        </span>
        <p className="text-muted">{texts.empty}</p>
        <ButtonLink href={`/${locale}`}>{texts.continue}</ButtonLink>
      </div>
    );
  }

  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <CartCountSync count={cart.itemCount} />
      <div className="flex flex-col gap-3 lg:col-span-2">
        {cart.hasProblems && (
          <p role="alert" className="rounded-xl border border-danger/40 bg-danger/5 p-3 text-sm text-danger">
            {texts.hasProblems}
          </p>
        )}
        <ul className="flex flex-col divide-y divide-border rounded-2xl border border-border bg-surface">
          {cart.lines.map((line) => {
            const problem = problemText(line, texts, locale);
            const href = productPath(locale, line.number, line.slug);
            return (
              <li key={line.id} className="flex gap-3 p-3 sm:gap-4 sm:p-4">
                <Link href={href} className="relative block h-20 w-20 shrink-0 overflow-hidden rounded-xl bg-surface-muted sm:h-24 sm:w-24">
                  {line.imageUrl && <Image src={imageVariant(line.imageUrl, 300)} alt="" width={96} height={96} unoptimized className="h-full w-full object-cover" />}
                </Link>
                <div className="flex min-w-0 flex-1 flex-col gap-2">
                  <Link href={href} className="line-clamp-2 text-sm font-medium hover:text-primary">
                    {line.name}
                  </Link>
                  {line.variantLabel && <p className="text-xs text-muted">{line.variantLabel}</p>}
                  {problem && <p className="text-xs font-medium text-danger">{problem}</p>}
                  <div className="flex flex-wrap items-end justify-between gap-2">
                    <CartQuantity
                      lineId={line.id}
                      quantity={line.quantity}
                      max={Math.max(line.quantity, line.available)}
                      name={line.name}
                      texts={texts}
                      errors={errors}
                    />
                    <div className="flex flex-col items-end text-end">
                      {line.compareAtPrice !== null && line.compareAtPrice > line.unitPrice && (
                        <del className="text-xs text-muted">{format(shownAmount(line.compareAtPrice, line.quantity, money))}</del>
                      )}
                      <span className="font-bold">{format(shownAmount(line.unitPrice, line.quantity, money))}</span>
                      {line.quantity > 1 && (
                        <span className="text-xs text-muted">{texts.each.replace("{price}", displayMoney(line.unitPrice, money, locale))}</span>
                      )}
                    </div>
                  </div>
                </div>
              </li>
            );
          })}
        </ul>
      </div>

      <aside aria-label={texts.summary} className="flex h-fit flex-col gap-4 rounded-2xl border border-border bg-surface p-4">
        <p className="text-sm text-muted">{texts.itemCount.replace("{count}", formatNumber(cart.itemCount, locale))}</p>
        <dl className="flex justify-between gap-2 font-bold">
          <dt>{texts.subtotal}</dt>
          <dd>{format(Number(total.toFixed(Math.max(money.decimals, 2))))}</dd>
        </dl>
        {approximate && <p className="text-xs text-muted">{approximate}</p>}
        <p className="text-xs text-muted">{texts.shippingLater}</p>
        {/* Changed lines are fixed in the cart first (checkout would refuse them). */}
        {cart.hasProblems ? (
          <Button size="lg" disabled aria-describedby="checkout-fix">
            {texts.checkout}
          </Button>
        ) : (
          <ButtonLink href={`/${locale}/checkout`} size="lg">
            {texts.checkout}
          </ButtonLink>
        )}
        {cart.hasProblems && (
          <p id="checkout-fix" className="text-xs text-muted">
            {texts.fixFirst}
          </p>
        )}
        <Link href={`/${locale}`} className="text-center text-sm text-primary hover:underline">
          {texts.continue}
        </Link>
      </aside>
    </div>
  );
}
