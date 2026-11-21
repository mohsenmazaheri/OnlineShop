import { describe, expect, it } from "vitest";
import { addEntry, comparePath, maxCompare, mergeLists, parseIds, parseStored, removeEntry } from "./list";

describe("comparison list rules", () => {
  it("adds once, up to four, from one category only", () => {
    const first = addEntry([], { n: 1, c: "phones" });
    expect(first).toEqual({ kind: "added", list: [{ n: 1, c: "phones" }] });
    const list = first.kind === "added" ? first.list : [];
    expect(addEntry(list, { n: 1, c: "phones" }).kind).toBe("present");
    expect(addEntry(list, { n: 2, c: "kitchen" }).kind).toBe("otherCategory");
    // An unknown category (from the account) never blocks.
    expect(addEntry([{ n: 1, c: "" }], { n: 2, c: "kitchen" }).kind).toBe("added");
    const full = [1, 2, 3, 4].map((n) => ({ n, c: "phones" }));
    expect(full).toHaveLength(maxCompare);
    expect(addEntry(full, { n: 5, c: "phones" }).kind).toBe("full");
    expect(removeEntry(full, 2).map((e) => e.n)).toEqual([1, 3, 4]);
  });

  it("merges the account's list with the guest's, account first", () => {
    expect(mergeLists([7, 3], [{ n: 3, c: "phones" }, { n: 9, c: "phones" }, { n: 10, c: "x" }, { n: 11, c: "x" }])).toEqual([
      { n: 7, c: "" },
      { n: 3, c: "phones" },
      { n: 9, c: "phones" },
      { n: 10, c: "x" },
    ]);
  });

  it("reads only well-formed stored lists", () => {
    expect(parseStored('{"list":[{"n":4,"c":"phones"},{"n":"x","c":1},{"n":-1,"c":""}],"account":true}')).toEqual({
      list: [{ n: 4, c: "phones" }],
      account: true,
    });
    expect(parseStored("not json")).toEqual({ list: [], account: false });
    expect(parseStored(null)).toEqual({ list: [], account: false });
  });

  it("reads and writes the shareable address", () => {
    expect(parseIds("12,15 12,abc,0,9,8,7")).toEqual([12, 15, 9, 8]);
    expect(parseIds(["3", "4"])).toEqual([3, 4]);
    expect(parseIds(undefined)).toEqual([]);
    expect(comparePath("fa", [12, 15])).toBe("/fa/compare?ids=12,15");
    expect(comparePath("en", [12], true)).toBe("/en/compare?ids=12&diff=1");
    expect(comparePath("en", [])).toBe("/en/compare");
  });
});
