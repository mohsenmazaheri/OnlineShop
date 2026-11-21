"use client";

import { useEffect, useSyncExternalStore } from "react";
import { compareStore, type CompareState } from "@/lib/compare/store";

const serverState: CompareState = { list: [], notice: null };
const noSubscribe = () => () => {};

/** The comparison list and its actions; the stored list is read once the first component mounts. */
export function useCompare() {
  const store = compareStore();
  useEffect(() => {
    void store.load();
  }, [store]);
  const state = useSyncExternalStore(store.subscribe, store.getState, () => serverState);
  return { state, store };
}

/** False while server-rendering and hydrating, then true: lets a no-JavaScript link become a button. */
export function useHydrated(): boolean {
  return useSyncExternalStore(
    noSubscribe,
    () => true,
    () => false,
  );
}
