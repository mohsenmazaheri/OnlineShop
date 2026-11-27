import type { OrderStatus, OrderView } from "@/lib/account/session";

/** The badge colour of a status (design-system tints). */
export function statusTint(status: OrderStatus): string {
  switch (status) {
    case "Pending":
      return "tint tint-4";
    case "Paid":
      return "tint tint-1";
    case "Shipped":
      return "tint tint-2";
    case "Completed":
      return "tint tint-0";
    default:
      return "bg-surface-muted text-muted";
  }
}

type HistoryEntry = OrderView["history"][number];
type HistoryTexts = {
  status: Record<OrderStatus, string>;
  historyPlaced: string;
  historyStatus: string;
  historyTracking: string;
  historyExpired: string;
};

/** One history line in words: "Placed", "Status: Shipped", "Tracking: Post RR123", or the note itself. */
export function historyText(entry: HistoryEntry, texts: HistoryTexts): string {
  switch (entry.kind) {
    case "Placed":
      return texts.historyPlaced;
    case "StatusChanged":
      return texts.historyStatus.replace("{status}", entry.status ? texts.status[entry.status] : "");
    case "TrackingSet":
      return texts.historyTracking.replace("{tracking}", entry.text ?? "");
    case "PaymentExpired":
      return texts.historyExpired;
    default:
      return entry.text ?? "";
  }
}

/** "/fa/account/orders/1001" or the guest's signed "/fa/order/1001?key=…" from the API's path below the language. */
export function orderHref(locale: string, path: string): string {
  return `/${locale}/${path.replace(/^\/+/, "")}`;
}

/** An order number in the reader's digits, never grouped ("۱۰۰۲", "1002"). */
export function formatOrderNumber(number: number, locale: string): string {
  return new Intl.NumberFormat(locale, { useGrouping: false }).format(number);
}
