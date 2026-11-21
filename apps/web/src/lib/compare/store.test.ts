import { describe, expect, it, vi } from "vitest";
import { createCompareStore, storageKey } from "./store";

function memoryStorage(initial?: unknown) {
  const items = new Map<string, string>();
  if (initial !== undefined) items.set(storageKey, JSON.stringify(initial));
  return {
    items,
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => void items.set(key, value),
    removeItem: (key: string) => void items.delete(key),
  };
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe("comparison store", () => {
  it("does not ask the account for an empty list and keeps a guest list in the browser", async () => {
    const storage = memoryStorage();
    const fetchImpl = vi.fn().mockResolvedValue(json({ numbers: [], signedIn: false }));
    const store = createCompareStore({ storage, fetchImpl });

    await store.load();
    expect(fetchImpl).not.toHaveBeenCalled();

    store.toggle({ n: 5, c: "phones" });
    await flush();
    expect(store.getState().list).toEqual([{ n: 5, c: "phones" }]);
    expect(JSON.parse(storage.items.get(storageKey)!)).toEqual({ list: [{ n: 5, c: "phones" }], account: false });
    // Asked once (a guest), never saved to the account.
    expect(fetchImpl).toHaveBeenCalledTimes(1);

    store.toggle({ n: 5, c: "phones" });
    expect(store.getState().list).toEqual([]);
    expect(storage.items.has(storageKey)).toBe(false);
  });

  it("asks before mixing categories or adding a fifth product", async () => {
    const store = createCompareStore({ storage: memoryStorage(), fetchImpl: vi.fn().mockResolvedValue(new Response(null, { status: 401 })) });
    store.toggle({ n: 1, c: "phones" });
    store.toggle({ n: 2, c: "kitchen" });
    expect(store.getState().notice).toEqual({ kind: "otherCategory", entry: { n: 2, c: "kitchen" } });
    store.startNew({ n: 2, c: "kitchen" });
    expect(store.getState()).toEqual({ list: [{ n: 2, c: "kitchen" }], notice: null });

    for (const n of [3, 4, 5]) store.toggle({ n, c: "kitchen" });
    store.toggle({ n: 6, c: "kitchen" });
    expect(store.getState().notice).toEqual({ kind: "full" });
    store.dismiss();
    expect(store.getState().notice).toBeNull();
    store.clear();
    expect(store.getState().list).toEqual([]);
  });

  it("merges a guest list into the account after signing in and saves it there", async () => {
    const storage = memoryStorage({ list: [{ n: 9, c: "phones" }], account: false });
    const fetchImpl = vi.fn().mockImplementation((_path: string, init?: RequestInit) =>
      Promise.resolve(init?.method === "PUT" ? json({ numbers: [7, 9], signedIn: true }) : json({ numbers: [7], signedIn: true })),
    );
    const store = createCompareStore({ storage, fetchImpl });

    await store.load();

    expect(store.getState().list).toEqual([{ n: 7, c: "" }, { n: 9, c: "phones" }]);
    const put = fetchImpl.mock.calls.find(([, init]) => init?.method === "PUT")!;
    expect(put[0]).toBe("/api/v1/account/compare");
    expect(JSON.parse(put[1].body as string)).toEqual({ numbers: [7, 9] });
    expect(JSON.parse(storage.items.get(storageKey)!).account).toBe(true);
  });

  it("follows the account's list and forgets it once signed out", async () => {
    const signedIn = createCompareStore({
      storage: memoryStorage({ list: [{ n: 3, c: "phones" }, { n: 4, c: "phones" }], account: true }),
      fetchImpl: vi.fn().mockResolvedValue(json({ numbers: [4], signedIn: true })),
    });
    await signedIn.load();
    // Removed on another device: gone here too; the cached category is kept.
    expect(signedIn.getState().list).toEqual([{ n: 4, c: "phones" }]);

    const storage = memoryStorage({ list: [{ n: 3, c: "phones" }], account: true });
    const signedOut = createCompareStore({ storage, fetchImpl: vi.fn().mockResolvedValue(new Response(null, { status: 401 })) });
    await signedOut.load();
    expect(signedOut.getState().list).toEqual([]);
    expect(storage.items.has(storageKey)).toBe(false);
  });

  it("keeps the stored list when the shop cannot be reached", async () => {
    const store = createCompareStore({
      storage: memoryStorage({ list: [{ n: 3, c: "phones" }], account: true }),
      fetchImpl: vi.fn().mockRejectedValue(new TypeError("offline")),
    });
    await store.load();
    expect(store.getState().list).toEqual([{ n: 3, c: "phones" }]);
    await store.load();
  });
});
