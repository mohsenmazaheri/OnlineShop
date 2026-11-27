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
  historyInvoiced: string;
  historyReturnRequested: string;
  historyReturnApproved: string;
  historyReturnRejected: string;
  historyReturnReceived: string;
  historyRefunded: string;
};

/**
 * One history line in words: "Placed", "Status: Shipped", "Tracking: Post RR123", "Return R-2 approved", "Refunded
 * €12", or the note itself. <paramref name="money"/> formats a refunded amount in the order's currency.
 */
export function historyText(entry: HistoryEntry, texts: HistoryTexts, money: (amount: number) => string = String): string {
  const text = entry.text ?? "";
  switch (entry.kind) {
    case "Invoiced":
      return texts.historyInvoiced.replace("{number}", text);
    case "ReturnRequested":
      return texts.historyReturnRequested.replace("{number}", text);
    case "ReturnApproved":
      return texts.historyReturnApproved.replace("{number}", text);
    case "ReturnRejected":
      return texts.historyReturnRejected.replace("{number}", text);
    case "ReturnReceived":
      return texts.historyReturnReceived.replace("{number}", text);
    case "Refunded":
      return texts.historyRefunded.replace("{amount}", Number.isFinite(Number(text)) ? money(Number(text)) : text);
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
