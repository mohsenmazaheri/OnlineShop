"use client";

import Link from "next/link";
import { useEffect, useState, type FormEvent } from "react";
import { FormAlert, FormField, SelectField } from "@/components/account/FormParts";
import { Button } from "@/components/ui/Button";
import { sendJson } from "@/lib/account/client";
import { messageFor, problemMessage, type ErrorTexts, type Problem } from "@/lib/account/problems";
import { notifyCartChanged } from "@/lib/cart/count";
import { addressFieldOf, addressFrom, deliveryDays, keepMethod, methodsFor, type CheckoutQuote, type PlacedOrder } from "@/lib/checkout/form";
import type { CheckoutView } from "@/lib/checkout/server";
import type { DisplayCurrency } from "@/lib/currency/display";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatMoney } from "@/lib/i18n/format-money";
import { joinParts } from "@/lib/i18n/format-list";
import { regionName } from "@/lib/i18n/display-names";
import { CheckoutPlaced } from "./CheckoutPlaced";

type Props = {
  locale: string;
  view: CheckoutView;
  /** The base currency: checkout shows what is charged, unconverted. */
  money: DisplayCurrency;
  texts: Dictionary["checkout"];
  addressTexts: Pick<Dictionary["account"], "recipientName" | "recipientPhone" | "country" | "region" | "city" | "line1" | "line2" | "postalCode">;
  errors: ErrorTexts;
  /** Where "sign in" leads (with the way back to checkout). */
  signInHref: string;
};

const NEW = "new";

/**
 * The checkout form: contact, delivery (method, then a saved or new address, or the pickup contact), discount code,
 * note, and the summary. Every total comes from the API's quote for the current choices; placing the order sends the
 * total the customer saw, so a price change in between is refused instead of charged.
 */
export function CheckoutForm({ locale, view, money, texts, addressTexts, errors, signInHref }: Props) {
  const defaultAddress = view.addresses.find((a) => a.isDefault) ?? view.addresses[0];
  const [addressId, setAddressId] = useState<string>(defaultAddress?.id ?? NEW);
  const [newCountry, setNewCountry] = useState(view.countries[0] ?? "");
  const country = addressId === NEW ? newCountry : (view.addresses.find((a) => a.id === addressId)?.countryCode ?? newCountry);
  const [methodId, setMethodId] = useState<string | null>(() => keepMethod(methodsFor(view.shippingMethods, country), null));
  const [codeInput, setCodeInput] = useState("");
  const [code, setCode] = useState<string | null>(null);
  const [quote, setQuote] = useState<CheckoutQuote | null>(null);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [placed, setPlaced] = useState<PlacedOrder | null>(null);

  async function requote(next: { country: string; methodId: string | null; code: string | null }) {
    const result = await sendJson<CheckoutQuote>("POST", `/api/v1/checkout/quote?locale=${locale}`, {
      countryCode: next.country || null,
      shippingMethodId: next.methodId,
      discountCode: next.code,
    });
    if (!result.ok) return;
    setQuote(result.data);
    const kept = keepMethod(result.data.shippingMethods, next.methodId);
    if (kept !== next.methodId) setMethodId(kept);
  }

  useEffect(() => {
    // The totals follow the choices; the first quote arrives right after the page shows.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- the quote is external data fetched for the current choices
    void requote({ country, methodId, code });
    // requote only reads its argument and stable setters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [country, methodId, code]);

  if (placed) return <CheckoutPlaced locale={locale} order={placed} money={money} texts={texts} />;

  const methods = quote?.shippingMethods ?? methodsFor(view.shippingMethods, country);
  const method = methods.find((m) => m.id === methodId) ?? null;
  const format = (amount: number) => formatMoney(amount, money.code, locale, { symbol: money.symbol, decimals: money.decimals });
  const fieldError = (field: string) => {
    const entry = Object.entries(problem?.fieldErrors ?? {}).find(([key]) => key === field || addressFieldOf(key) === field);
    return entry ? messageFor(entry[1][0], errors) : undefined;
  };
  const formError = problem && (problem.code || Object.keys(problem.fieldErrors).length === 0) ? problemMessage(problem, errors) : null;
  const discountError = quote?.discountError ? messageFor(quote.discountError, errors) : null;

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!quote || !method) return;
    const form = new FormData(event.currentTarget);
    const text = (name: string) => String(form.get(name) ?? "").trim() || null;
    setBusy(true);
    setProblem(null);
    const result = await sendJson<PlacedOrder>("POST", `/api/v1/checkout/orders?locale=${locale}`, {
      shippingMethodId: method.id,
      addressId: !method.isPickup && addressId !== NEW ? addressId : null,
      address: !method.isPickup && addressId === NEW ? addressFrom(form) : null,
      saveAddress: form.get("saveAddress") === "on",
      email: text("email"),
      pickupName: method.isPickup ? text("pickupName") : null,
      pickupPhone: method.isPickup ? text("pickupPhone") : null,
      discountCode: quote.discountApplied ? code : null,
      note: text("note"),
      expectedTotal: quote.totals.total,
    });
    setBusy(false);
    if (result.ok) {
      setPlaced(result.data);
      notifyCartChanged();
      window.scrollTo({ top: 0 });
      return;
    }
    setProblem(result.problem);
    // Prices or the code changed meanwhile: show the new totals with the message.
    if (result.problem.code === "checkout.total_changed" || result.problem.code?.startsWith("checkout.discount")) void requote({ country, methodId, code });
  }

  const section = "flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4";
  return (
    <form onSubmit={onSubmit} noValidate className="grid gap-6 lg:grid-cols-3">
      <div className="flex flex-col gap-4 lg:col-span-2">
        <section className={section} aria-labelledby="checkout-contact">
          <h2 id="checkout-contact" className="font-bold">
            {texts.contactTitle}
          </h2>
          {view.signedIn ? (
            <p className="text-sm text-muted">{texts.signedInAs.replace("{name}", view.contact?.email ?? view.contact?.phoneNumber ?? "")}</p>
          ) : (
            <p className="text-sm">
              {texts.signInPrompt}{" "}
              <Link href={signInHref} className="font-medium text-primary hover:underline">
                {texts.signIn}
              </Link>
            </p>
          )}
          <FormField
            label={view.emailRequired ? texts.email : texts.emailOptional}
            name="email"
            type="email"
            dir="ltr"
            autoComplete="email"
            required={view.emailRequired}
            maxLength={256}
            defaultValue={view.contact?.email ?? ""}
            hint={texts.emailHint}
            error={fieldError("email")}
          />
        </section>

        <section className={section} aria-labelledby="checkout-method">
          <h2 id="checkout-method" className="font-bold">
            {texts.methodTitle}
          </h2>
          {methods.length === 0 && <p className="text-sm text-danger">{texts.noMethods}</p>}
          <div className="flex flex-col gap-2" role="radiogroup" aria-labelledby="checkout-method">
            {methods.map((m) => {
              const days = deliveryDays(m.minDays, m.maxDays, texts);
              return (
                <label key={m.id} className="flex cursor-pointer items-start gap-3 rounded-xl border border-border p-3 has-[:checked]:border-primary has-[:checked]:bg-primary/5">
                  <input type="radio" name="method" value={m.id} checked={m.id === methodId} onChange={() => setMethodId(m.id)} className="mt-1 h-4 w-4 accent-[var(--primary)]" />
                  <span className="flex flex-1 flex-col gap-0.5">
                    <span className="flex justify-between gap-2 font-medium">
                      <span>{m.name}</span>
                      <span>{m.cost === 0 ? texts.free : format(m.cost)}</span>
                    </span>
                    {(m.description || days) && <span className="text-xs text-muted">{[m.description, days].filter(Boolean).join(" · ")}</span>}
                  </span>
                </label>
              );
            })}
          </div>
        </section>

        {method?.isPickup ? (
          <section className={section} aria-labelledby="checkout-pickup">
            <h2 id="checkout-pickup" className="font-bold">
              {texts.pickupTitle}
            </h2>
            <FormField label={texts.pickupName} name="pickupName" autoComplete="name" required maxLength={100} defaultValue={view.contact?.name ?? ""} error={fieldError("pickupName")} />
            <FormField label={texts.pickupPhone} name="pickupPhone" type="tel" dir="ltr" autoComplete="tel" required maxLength={32} defaultValue={view.contact?.phoneNumber ?? ""} error={fieldError("pickupPhone")} />
          </section>
        ) : (
          <section className={section} aria-labelledby="checkout-address">
            <h2 id="checkout-address" className="font-bold">
              {texts.addressTitle}
            </h2>
            {view.addresses.length > 0 && (
              <div className="flex flex-col gap-2" role="radiogroup" aria-labelledby="checkout-address">
                {view.addresses.map((a) => (
                  <label key={a.id} className="flex cursor-pointer items-start gap-3 rounded-xl border border-border p-3 text-sm has-[:checked]:border-primary has-[:checked]:bg-primary/5">
                    <input type="radio" name="addressId" value={a.id} checked={addressId === a.id} onChange={() => setAddressId(a.id)} className="mt-1 h-4 w-4 accent-[var(--primary)]" />
                    <span>
                      <span className="font-medium">{a.label ?? a.recipientName}</span>
                      <span className="block text-muted">
                        {joinParts([a.line1, a.line2, a.city, a.region, a.postalCode, regionName(a.countryCode, locale)], locale)}
                      </span>
                    </span>
                  </label>
                ))}
                <label className="flex cursor-pointer items-center gap-3 rounded-xl border border-border p-3 text-sm has-[:checked]:border-primary">
                  <input type="radio" name="addressId" value={NEW} checked={addressId === NEW} onChange={() => setAddressId(NEW)} className="h-4 w-4 accent-[var(--primary)]" />
                  {texts.newAddress}
                </label>
              </div>
            )}
            {addressId === NEW && (
              <div className="grid gap-3 sm:grid-cols-2">
                <FormField label={addressTexts.recipientName} name="address.recipientName" id="address-recipient" autoComplete="shipping name" required maxLength={100} defaultValue={view.contact?.name ?? ""} error={fieldError("recipientName")} />
                <FormField label={addressTexts.recipientPhone} name="address.phone" id="address-phone" type="tel" dir="ltr" autoComplete="shipping tel" required maxLength={32} defaultValue={view.contact?.phoneNumber ?? ""} error={fieldError("phone")} />
                <SelectField
                  label={addressTexts.country}
                  name="address.countryCode"
                  id="address-country"
                  autoComplete="shipping country"
                  value={newCountry}
                  onChange={(e) => setNewCountry(e.target.value)}
                  options={view.countries.map((c) => ({ value: c, label: regionName(c, locale) }))}
                />
                <FormField label={addressTexts.region} name="address.region" id="address-region" autoComplete="shipping address-level1" maxLength={100} error={fieldError("region")} />
                <FormField label={addressTexts.city} name="address.city" id="address-city" autoComplete="shipping address-level2" required maxLength={100} error={fieldError("city")} />
                <FormField label={addressTexts.postalCode} name="address.postalCode" id="address-postal" dir="ltr" inputMode="numeric" autoComplete="shipping postal-code" maxLength={20} error={fieldError("postalCode")} />
                <div className="sm:col-span-2">
                  <FormField label={addressTexts.line1} name="address.line1" id="address-line1" autoComplete="shipping address-line1" required maxLength={300} error={fieldError("line1")} />
                </div>
                <div className="sm:col-span-2">
                  <FormField label={addressTexts.line2} name="address.line2" id="address-line2" autoComplete="shipping address-line2" maxLength={300} error={fieldError("line2")} />
                </div>
                {view.signedIn && (
                  <label className="flex items-center gap-2 text-sm sm:col-span-2">
                    <input type="checkbox" name="saveAddress" defaultChecked className="h-4 w-4 accent-[var(--primary)]" />
                    {texts.saveAddress}
                  </label>
                )}
              </div>
            )}
          </section>
        )}

        <section className={section} aria-labelledby="checkout-note">
          <h2 id="checkout-note" className="font-bold">
            {texts.noteTitle}
          </h2>
          <label htmlFor="checkout-note-text" className="sr-only">
            {texts.noteTitle}
          </label>
          <textarea id="checkout-note-text" name="note" rows={3} maxLength={1000} placeholder={texts.notePlaceholder} className="rounded-xl border border-border bg-surface p-3 outline-none focus:border-primary" />
        </section>
      </div>

      <aside aria-label={texts.summaryTitle} className="flex h-fit flex-col gap-4 rounded-2xl border border-border bg-surface p-4 lg:sticky lg:top-24">
        <h2 className="font-bold">{texts.summaryTitle}</h2>
        <ul className="flex flex-col gap-2 text-sm">
          {view.cart.lines.map((l) => (
            <li key={l.id} className="flex justify-between gap-3">
              <span className="min-w-0">
                <span className="line-clamp-2">{l.name}</span>
                <span className="text-xs text-muted">
                  {[l.variantLabel, `× ${new Intl.NumberFormat(locale).format(l.quantity)}`].filter(Boolean).join(" · ")}
                </span>
              </span>
              <span className="shrink-0">{format(l.lineTotal)}</span>
            </li>
          ))}
        </ul>

        <div className="flex flex-col gap-2 border-t border-border pt-3">
          <label htmlFor="checkout-code" className="text-sm font-medium">
            {texts.discountTitle}
          </label>
          <div className="flex gap-2">
            <input
              id="checkout-code"
              value={codeInput}
              onChange={(e) => setCodeInput(e.target.value)}
              dir="ltr"
              maxLength={40}
              autoComplete="off"
              aria-describedby={discountError ? "checkout-code-error" : undefined}
              className="h-11 min-w-0 flex-1 rounded-xl border border-border bg-surface px-3 uppercase outline-none focus:border-primary"
            />
            <Button variant="secondary" onClick={() => setCode(codeInput.trim() || null)}>
              {texts.discountApply}
            </Button>
          </div>
          {discountError && (
            <p id="checkout-code-error" className="text-xs text-danger">
              {discountError}
            </p>
          )}
          {quote?.discountApplied && code && (
            <p className="flex items-center justify-between text-xs text-success">
              {texts.discountApplied.replace("{code}", code.toUpperCase())}
              <button
                type="button"
                className="text-muted underline"
                onClick={() => {
                  setCode(null);
                  setCodeInput("");
                }}
              >
                {texts.discountRemove}
              </button>
            </p>
          )}
        </div>

        {quote && (
          <dl className="flex flex-col gap-1.5 border-t border-border pt-3 text-sm">
            <div className="flex justify-between">
              <dt className="text-muted">{texts.subtotal}</dt>
              <dd>{format(quote.totals.subtotal)}</dd>
            </div>
            {quote.totals.discount > 0 && (
              <div className="flex justify-between text-success">
                <dt>{texts.discount}</dt>
                <dd>−{format(quote.totals.discount)}</dd>
              </div>
            )}
            <div className="flex justify-between">
              <dt className="text-muted">{texts.shipping}</dt>
              <dd>{method ? (quote.totals.shipping === 0 ? texts.free : format(quote.totals.shipping)) : texts.shippingChoose}</dd>
            </div>
            {quote.totals.taxRate > 0 && !quote.totals.pricesIncludeTax && (
              <div className="flex justify-between">
                <dt className="text-muted">{texts.taxAdded.replace("{rate}", new Intl.NumberFormat(locale).format(quote.totals.taxRate))}</dt>
                <dd>{format(quote.totals.tax)}</dd>
              </div>
            )}
            <div className="flex justify-between border-t border-border pt-2 text-base font-bold">
              <dt>{texts.total}</dt>
              <dd>{format(quote.totals.total)}</dd>
            </div>
            {quote.totals.taxRate > 0 && quote.totals.pricesIncludeTax && (
              <p className="text-xs text-muted">
                {texts.taxIncluded.replace("{rate}", new Intl.NumberFormat(locale).format(quote.totals.taxRate)).replace("{amount}", format(quote.totals.tax))}
              </p>
            )}
          </dl>
        )}
        <p className="text-xs text-muted">{texts.currencyNote.replace("{currency}", money.code)}</p>
        {formError && <FormAlert kind="error">{formError}</FormAlert>}
        <Button type="submit" size="lg" disabled={busy || !quote || !method || quote.hasProblems}>
          {busy ? texts.placing : texts.place}
        </Button>
        <Link href={`/${locale}/cart`} className="text-center text-sm text-primary hover:underline">
          {texts.backToCart}
        </Link>
      </aside>
    </form>
  );
}

