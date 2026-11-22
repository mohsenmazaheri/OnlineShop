import type { components } from "@/lib/api/schema";

export type ShippingOption = components["schemas"]["ShippingOption"];
export type CheckoutQuote = components["schemas"]["CheckoutQuote"];
export type PlacedOrder = components["schemas"]["PlacedOrderView"];
export type AddressInput = components["schemas"]["AddressInput"];

/** The methods that ship to a country (pickup and "every country" methods always do), in the admin's order. */
export function methodsFor(methods: readonly ShippingOption[], country: string): ShippingOption[] {
  return methods.filter((m) => m.isPickup || m.countries.length === 0 || m.countries.includes(country));
}

/** The method to keep selected: the current one if it is still offered, else the first one. */
export function keepMethod(methods: readonly ShippingOption[], current: string | null): string | null {
  return methods.some((m) => m.id === current) ? current : (methods[0]?.id ?? null);
}

/** "2–4" style delivery time texts; null without any days. */
export function deliveryDays(min: number | null, max: number | null, texts: { days: string; daysOne: string }): string | null {
  if (min === null && max === null) return null;
  if (min === null || max === null || min === max) return texts.daysOne.replace("{days}", String(min ?? max));
  return texts.days.replace("{min}", String(min)).replace("{max}", String(max));
}

/** The typed address from the checkout form's fields (named address.*). */
export function addressFrom(form: FormData): AddressInput {
  const text = (name: string) => String(form.get(`address.${name}`) ?? "").trim();
  return {
    label: null,
    recipientName: text("recipientName"),
    phone: text("phone"),
    countryCode: text("countryCode"),
    region: text("region") || null,
    city: text("city"),
    line1: text("line1"),
    line2: text("line2") || null,
    postalCode: text("postalCode") || null,
    isDefault: false,
  };
}

/** The API reports nested fields as "address.City": the form's field name for it ("city"). */
export function addressFieldOf(apiField: string): string | null {
  const match = /^address\.(\w+)$/i.exec(apiField);
  return match ? match[1].charAt(0).toLowerCase() + match[1].slice(1) : null;
}
