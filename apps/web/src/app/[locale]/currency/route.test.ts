import { NextRequest } from "next/server";
import { describe, expect, it } from "vitest";
import { GET } from "./route";

const call = (url: string, locale = "fa") =>
  GET(new NextRequest(url), { params: Promise.resolve({ locale }) } as Parameters<typeof GET>[1]);

describe("GET /{locale}/currency", () => {
  it("remembers the currency for this language and goes back", async () => {
    const response = await call("http://localhost:3000/fa/currency?code=usd&returnTo=/fa/p/1001/x");

    expect(response.status).toBe(303);
    expect(response.headers.get("location")).toBe("http://localhost:3000/fa/p/1001/x");
    const cookie = response.headers.get("set-cookie") ?? "";
    expect(cookie).toContain("os_currency=fa%3AUSD");
    expect(cookie.toLowerCase()).toContain("httponly");
  });

  it("never redirects outside the shop or to another language", async () => {
    for (const returnTo of ["https://evil.example", "//evil.example", "/en/p/1"]) {
      const response = await call(`http://localhost:3000/fa/currency?code=USD&returnTo=${encodeURIComponent(returnTo)}`);
      expect(response.headers.get("location")).toBe("http://localhost:3000/fa");
    }
  });

  it("sets nothing for a malformed code and 404s an unknown language", async () => {
    expect((await call("http://localhost:3000/fa/currency?code=<x>")).headers.get("set-cookie")).toBeNull();
    expect((await call("http://localhost:3000/xx/currency?code=USD", "xx")).status).toBe(404);
  });
});
