import { describe, expect, it } from "vitest";
import { pageWindow } from "./pagination";

describe("pageWindow", () => {
  it("shows the ends and two pages either side", () => {
    expect(pageWindow(1, 1)).toEqual([1]);
    expect(pageWindow(10, 40)).toEqual([1, 8, 9, 10, 11, 12, 40]);
  });
});
