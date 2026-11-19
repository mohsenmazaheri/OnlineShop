import { describe, expect, it } from "vitest";
import { languageNames } from "./language-names";

describe("languageNames", () => {
  it("names each language in itself", () => {
    expect(languageNames(["fa", "de"])).toEqual({ fa: "فارسی", de: "Deutsch" });
  });
});
