// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ContentList } from "@/lib/account/session";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { ContentEditor, parseNumbers, readContentTexts } from "./ContentEditor";

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

const list: ContentList = {
  defaultLocale: "fa",
  locales: ["fa", "en"],
  items: [
    {
      id: "c1",
      kind: "Page",
      texts: [
        { locale: "fa", title: "درباره ما", slug: "about", summary: null, body: "ما فروشگاهی کوچک هستیم.", metaTitle: null, metaDescription: null, tags: null },
        { locale: "en", title: "", slug: null, summary: null, body: "", metaTitle: null, metaDescription: null, tags: null },
      ],
      authorName: null,
      relatedProducts: [1001],
      isPublished: true,
      publishedUtc: "2026-11-20T10:00:00Z",
      showInFooter: true,
      coverImageUrl: null,
      modifiedUtc: "2026-11-20T10:00:00Z",
    },
  ],
};

describe("content form helpers", () => {
  it("read product numbers and texts per language", () => {
    expect(parseNumbers("1001, 1002 x 1001،1003")).toEqual([1001, 1002, 1003]);
    const form = new FormData();
    form.set("p-title-fa", " عنوان ");
    form.set("p-body-fa", "متن");
    expect(readContentTexts(form, ["fa", "en"], "p-")).toEqual([
      { locale: "fa", title: "عنوان", slug: null, summary: null, body: "متن", metaTitle: null, metaDescription: null, tags: null },
      { locale: "en", title: "", slug: null, summary: null, body: "", metaTitle: null, metaDescription: null, tags: null },
    ]);
  });
});

describe("ContentEditor", () => {
  it("saves a page with its footer link and refreshes the storefront's content", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(list.items[0]), { status: 200 }));
    render(<ContentEditor locale="en" texts={en.admin} list={list} languageNames={{ fa: "فارسی", en: "English" }} />);

    fireEvent.click(screen.getByRole("button", { name: /درباره ما/ }));
    expect(screen.getByRole("link", { name: en.admin.landingOpen }).getAttribute("href")).toBe("/en/about");
    expect((screen.getByRole("checkbox", { name: en.admin.contentInFooter }) as HTMLInputElement).checked).toBe(true);
    // Pages have no tags field; posts do.
    expect(screen.queryByLabelText(en.admin.contentTags)).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: en.admin.landingSave }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    const [path, init] = fetchMock.mock.calls[0];
    expect(path).toBe("/api/v1/admin/content/c1");
    const body = JSON.parse(init.body as string);
    expect(body).toMatchObject({ kind: "Page", relatedProducts: [1001], isPublished: true, showInFooter: true });
    expect(body.texts[0]).toMatchObject({ locale: "fa", title: "درباره ما", slug: "about", body: "ما فروشگاهی کوچک هستیم." });
    expect(JSON.parse(fetchMock.mock.calls[1][1].body as string)).toEqual({ tags: ["content"] });
  });

  it("starts with an empty post form when there is nothing yet", () => {
    render(<ContentEditor locale="en" texts={en.admin} list={{ ...list, items: [] }} languageNames={{}} />);
    expect(screen.getByText(en.admin.contentEmpty)).toBeTruthy();
    expect(screen.getAllByLabelText(en.admin.contentTags)).toHaveLength(2);
  });
});
