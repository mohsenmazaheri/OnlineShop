"use client";

import { useSyncExternalStore } from "react";

const noSubscribe = () => () => {};

/** False while server-rendering and hydrating, then true: lets a no-JavaScript link become a button, a browser-only value appear. */
export function useHydrated(): boolean {
  return useSyncExternalStore(
    noSubscribe,
    () => true,
    () => false,
  );
}
