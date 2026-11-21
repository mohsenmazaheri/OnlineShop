import { sendJson } from "@/lib/account/client";
import { addEntry, mergeLists, parseStored, removeEntry, type CompareEntry } from "./list";

/** Why the last add did not happen: the bar asks the shopper what to do. */
export type CompareNotice = { kind: "full" } | { kind: "otherCategory"; entry: CompareEntry } | null;

export type CompareState = { list: CompareEntry[]; notice: CompareNotice };

export const storageKey = "os_compare";
const accountPath = "/api/v1/account/compare";

type Deps = { storage: Pick<Storage, "getItem" | "setItem" | "removeItem"> | null; fetchImpl: typeof fetch };

/**
 * The comparison list in the browser. A guest's list lives in localStorage; a signed-in customer's list lives in the
 * account (and is cached locally with the categories). The account is asked only once there is something to keep (a
 * list in storage, or the first add), so pages do not call the API for every visitor. Then a guest list is merged into
 * the account's after signing in, and a cached account list is dropped once signed out.
 */
export function createCompareStore({ storage, fetchImpl }: Deps) {
  let state: CompareState = { list: [], notice: null };
  let signedIn = false;
  let loaded = false;
  let syncing: Promise<void> | null = null;
  const listeners = new Set<() => void>();

  function set(next: Partial<CompareState>) {
    state = { ...state, ...next };
    for (const listener of listeners) listener();
  }

  function read() {
    try {
      return parseStored(storage?.getItem(storageKey) ?? null);
    } catch {
      return parseStored(null);
    }
  }

  function write(list: CompareEntry[]) {
    try {
      if (list.length === 0) storage?.removeItem(storageKey);
      else storage?.setItem(storageKey, JSON.stringify({ list, account: signedIn }));
    } catch {
      // Private mode or blocked storage: the list lasts for this page only.
    }
  }

  /** Finds out whether the visitor is signed in, and brings the stored and the account's lists together. */
  function sync(): Promise<void> {
    syncing ??= (async () => {
      const stored = read();
      let response: Response | null = null;
      try {
        response = await fetchImpl(accountPath, { credentials: "same-origin", headers: { Accept: "application/json" } });
      } catch {
        response = null;
      }
      const answer = response?.ok ? ((await response.json()) as { numbers: number[]; signedIn: boolean }) : null;
      if (answer?.signedIn) {
        signedIn = true;
        const numbers = answer.numbers;
        // A cached account list follows the account (changed elsewhere); a guest list is added to it.
        const list = stored.account
          ? numbers.map((n) => ({ n, c: stored.list.find((e) => e.n === n)?.c ?? "" }))
          : mergeLists(numbers, stored.list);
        const changed = list.length !== numbers.length || list.some((e, i) => e.n !== numbers[i]);
        // Products added on this page while asking stay (not the stored ones: the account decides about those).
        const added = state.list.filter((e) => !stored.list.some((s) => s.n === e.n));
        const merged = mergeLists(list.map((e) => e.n), [...list, ...added]);
        write(merged);
        set({ list: merged });
        if (changed || merged.length !== list.length) await save(merged);
      } else if (answer || response?.status === 401) {
        // A guest (signedIn: false).
        signedIn = false;
        if (stored.account) {
          write([]);
          set({ list: state.list.filter((e) => !stored.list.some((s) => s.n === e.n)) });
        }
      }
    })();
    return syncing;
  }

  async function save(list: CompareEntry[]) {
    if (signedIn) await sendJson("PUT", accountPath, { numbers: list.map((e) => e.n) }, fetchImpl);
  }

  function commit(list: CompareEntry[]) {
    write(list);
    set({ list, notice: null });
    void sync().then(() => save(state.list));
  }

  return {
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    getState: () => state,
    /** Reads the stored list once (on the first component that needs it). */
    load() {
      if (loaded) return Promise.resolve();
      loaded = true;
      const stored = read();
      set({ list: stored.list });
      return stored.list.length > 0 ? sync() : Promise.resolve();
    },
    has: (number: number) => state.list.some((e) => e.n === number),
    toggle(entry: CompareEntry) {
      if (state.list.some((e) => e.n === entry.n)) {
        commit(removeEntry(state.list, entry.n));
        return;
      }
      const outcome = addEntry(state.list, entry);
      if (outcome.kind === "added") commit(outcome.list);
      else if (outcome.kind === "full") set({ notice: { kind: "full" } });
      else if (outcome.kind === "otherCategory") set({ notice: { kind: "otherCategory", entry } });
    },
    remove: (number: number) => commit(removeEntry(state.list, number)),
    clear: () => commit([]),
    /** Replaces the list with this product (the answer to "another category"). */
    startNew: (entry: CompareEntry) => commit([entry]),
    dismiss: () => set({ notice: null }),
  };
}

export type CompareStore = ReturnType<typeof createCompareStore>;

let shared: CompareStore | null = null;

/** The page's one store (browser only). */
export function compareStore(): CompareStore {
  shared ??= createCompareStore({ storage: typeof window === "undefined" ? null : safeStorage(), fetchImpl: (...args) => fetch(...args) });
  return shared;
}

function safeStorage() {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}
