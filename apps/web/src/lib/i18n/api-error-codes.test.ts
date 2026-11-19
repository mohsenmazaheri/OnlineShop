import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { supportedLocales } from "./config";
import { getDictionary } from "./dictionaries";

/**
 * Error codes the API can return with the given prefix ("account", "staff"), read from every C# file of the
 * Application project (not bin/obj), so a new code without a translation fails here.
 */
function apiErrorCodes(prefix: string): string[] {
  const root = fileURLToPath(new URL("../../../../../src/OnlineShopping.Application/", import.meta.url));
  // Authorization/ holds permission names such as "staff.manage", which are not error codes.
  const files = readdirSync(root, { recursive: true, encoding: "utf8" }).filter(
    (file) => file.endsWith(".cs") && !/(^|[\\/])(bin|obj|Authorization)[\\/]/.test(file),
  );
  const pattern = new RegExp(`= "${prefix}\\.([a-z0-9_]+)"`, "g");
  const codes = files.flatMap((file) => [...readFileSync(root + file, "utf8").matchAll(pattern)].map((m) => m[1]));
  return [...new Set(codes)];
}

/** Which dictionary section translates which code prefix. */
const sections = [
  { prefix: "account", errors: async (locale: (typeof supportedLocales)[number]) => (await getDictionary(locale)).account.errors },
  { prefix: "staff", errors: async (locale: (typeof supportedLocales)[number]) => (await getDictionary(locale)).admin.errors },
  { prefix: "currency", errors: async (locale: (typeof supportedLocales)[number]) => (await getDictionary(locale)).admin.currencyErrors },
  { prefix: "catalog", errors: async (locale: (typeof supportedLocales)[number]) => (await getDictionary(locale)).admin.catalogErrors },
  { prefix: "media", errors: async (locale: (typeof supportedLocales)[number]) => (await getDictionary(locale)).admin.mediaErrors },
  { prefix: "import", errors: async (locale: (typeof supportedLocales)[number]) => (await getDictionary(locale)).admin.importErrors },
  { prefix: "seo", errors: async (locale: (typeof supportedLocales)[number]) => (await getDictionary(locale)).admin.seoErrors },
  { prefix: "branding", errors: async (locale: (typeof supportedLocales)[number]) => (await getDictionary(locale)).admin.brandingErrors },
] as const;

for (const { prefix, errors } of sections) {
  describe(`API error codes ${prefix}.*`, () => {
    it("are found in the C# source", () => {
      expect(apiErrorCodes(prefix).length).toBeGreaterThan(0);
    });

    it.each(supportedLocales)("all have a %s message", async (locale) => {
      const texts = (await errors(locale)) as Record<string, string>;
      const missing = apiErrorCodes(prefix).filter((code) => !texts[code]);

      expect(missing).toEqual([]);
    });
  });
}
