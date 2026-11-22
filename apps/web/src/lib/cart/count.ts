/**
 * The cart's piece count, from the small readable cookie the API sets on every cart answer (os_cart_n), so the header
 * badge needs no request. Components that change the cart call {@link notifyCartChanged} after the API answered.
 */
export const countCookie = "os_cart_n";
const changed = "os-cart-changed";

/** The count in a cookie header ("a=1; os_cart_n=3" → 3); 0 without it. */
export function readCount(cookie: string): number {
  const match = new RegExp(`(?:^|;\\s*)${countCookie}=(\\d{1,4})`).exec(cookie);
  return match ? Number(match[1]) : 0;
}

export function subscribeCount(listener: () => void): () => void {
  window.addEventListener(changed, listener);
  window.addEventListener("focus", listener);
  return () => {
    window.removeEventListener(changed, listener);
    window.removeEventListener("focus", listener);
  };
}

export function currentCount(): number {
  return readCount(document.cookie);
}

export function notifyCartChanged() {
  window.dispatchEvent(new Event(changed));
}

/**
 * Sets the count cookie from a cart the server rendered (that answer's Set-Cookie never reached the browser, e.g.
 * right after signing in merged the guest cart). Same attributes as the API's: path /, 60 days, Lax.
 */
export function writeCount(count: number) {
  if (currentCount() === count) return;
  // Secure on https, like the API's (a plain cookie may not replace a secure one).
  const attributes = `path=/; samesite=lax${location.protocol === "https:" ? "; secure" : ""}`;
  document.cookie = count > 0 ? `${countCookie}=${count}; max-age=${60 * 24 * 3600}; ${attributes}` : `${countCookie}=; max-age=0; ${attributes}`;
  notifyCartChanged();
}
