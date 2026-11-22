"use client";

import { useEffect } from "react";
import { writeCount } from "@/lib/cart/count";

/** Brings the header badge in line with the cart the page shows. Renders nothing. */
export function CartCountSync({ count }: { count: number }) {
  useEffect(() => writeCount(count), [count]);
  return null;
}
