// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { LandingPageList } from "@/lib/account/session";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { LandingPagesEditor } from "./LandingPagesEditor";

const fetchMock = vi.fn();
beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const list: LandingPageList = {
  defaultLocale: "fa",
  locales: ["fa", "en"],
  pages: [
    {
      id: "p1",
      texts: [
        { locale: "fa", name: "گوشی اورویا", slug: "گوشی-اورویا", description: null },
        { locale: "en", name: "Orvia phones", slug: "orvia-phones", description: "The best." },
      ],
      seo: [
        { locale: "fa", metaTitle: null, metaDescription: null, tags: null },
        { locale: "en", metaTitle: "Orvia phones | Shop", metaDescription: null, tags: null },
      ],
      query: "c=electronics&brand=orvia",
      isPublished: true,
    },
  ],
};

describe("LandingPagesEditor", () => {
  it("opens a page, saves its texts, query and search texts, and refreshes the storefront", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(list.pages[0]), { status: 200 }));
    render(<LandingPagesEditor locale="en" texts={en.admin} list={list} languageNames={{ fa: "فارسی", en: "English" }} />);

    fireEvent.click(screen.getByRole("button", { name: /گوشی اورویا/ }));
    expect((screen.getByDisplayValue("c=electronics&brand=orvia") as HTMLInputElement).name).toBe("query");
    expect(screen.getByRole("link", { name: en.admin.landingOpen }).getAttribute("href")).toBe("/en/l/orvia-phones");
    fireEvent.click(screen.getByRole("button", { name: en.admin.landingSave }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    const [path, init] = fetchMock.mock.calls[0];
    expect(path).toBe("/api/v1/admin/landing-pages/p1");
    expect(init.method).toBe("PUT");
    const body = JSON.parse(init.body as string);
    expect(body.query).toBe("c=electronics&brand=orvia");
    expect(body.isPublished).toBe(true);
    expect(body.texts[1]).toEqual({ locale: "en", name: "Orvia phones", slug: "orvia-phones", description: "The best." });
    expect(body.seo[1]).toEqual({ locale: "en", metaTitle: "Orvia phones | Shop", metaDescription: null, tags: null });
    expect(JSON.parse(fetchMock.mock.calls[1][1].body as string)).toEqual({ tags: ["catalog"] });
  });

  it("starts with a new page form when there is none", () => {
    render(<LandingPagesEditor locale="en" texts={en.admin} list={{ ...list, pages: [] }} languageNames={{}} />);
    expect(screen.getByText(en.admin.landingEmpty)).toBeTruthy();
    expect(screen.getByRole("button", { name: en.admin.landingSave })).toBeTruthy();
  });
});
