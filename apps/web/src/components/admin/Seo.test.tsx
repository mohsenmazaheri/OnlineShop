// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn(), replace: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { applyLiveInput } from "./ProductEditor";
import { RedirectsEditor } from "./RedirectsEditor";
import { SeoFields, readSeoFields } from "./SeoFields";
import { SynonymsEditor, splitTerms } from "./SynonymsEditor";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const apiCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).startsWith("/api/v1/"));
const bodyOf = (i = 0) => JSON.parse(String((apiCalls()[i][1] as RequestInit).body));
const names = { fa: "فارسی", en: "English" };

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("SeoFields", () => {
  const props = {
    locales: ["fa", "en"],
    defaultLocale: "fa",
    names: { fa: "کوله‌پشتی", en: "" },
    slugs: { fa: "کوله", en: "" },
    descriptions: { fa: "", en: "" },
    number: 1001,
    shopName: "Shop",
    siteUrl: "https://shop.test",
    imageCount: 1,
    imagesWithoutAlt: { fa: 1, en: 1 },
    languageNames: names,
    texts,
  };

  it("previews the result from the product texts and warns about what is missing", () => {
    render(<SeoFields {...props} values={[]} />);

    const fa = screen.getByRole("group", { name: "فارسی" });
    expect(within(fa).getByText("کوله‌پشتی | Shop")).toBeTruthy();
    expect(within(fa).getByText("https://shop.test/fa/p/1001/%DA%A9%D9%88%D9%84%D9%87")).toBeTruthy();
    expect(within(fa).getByText(texts.seoWarnings.descriptionMissing)).toBeTruthy();
    expect(within(fa).getByText(texts.seoWarnings.imageAltMissing.replace("{count}", "1"))).toBeTruthy();
    // English has no translated name: its page is not indexed, and that is the only remark.
    const enGroup = screen.getByRole("group", { name: "English" });
    expect(within(enGroup).getAllByRole("listitem").map((li) => li.textContent)).toEqual([`⚠${texts.seoWarnings.notTranslated}`]);
  });

  it("uses the search texts when written, and reads them back from the form", () => {
    const { container } = render(
      <form>
        <SeoFields {...props} values={[{ locale: "fa", metaTitle: "کوله ۲۵ لیتری", metaDescription: "توضیح", tags: "کیف" }]} />
      </form>,
    );

    expect(screen.getByText("کوله ۲۵ لیتری | Shop")).toBeTruthy();
    const seo = readSeoFields(new FormData(container.querySelector("form")!), ["fa", "en"]);
    expect(seo).toEqual([
      { locale: "fa", metaTitle: "کوله ۲۵ لیتری", metaDescription: "توضیح", tags: "کیف" },
      { locale: "en", metaTitle: null, metaDescription: null, tags: null },
    ]);
  });

  it("follows what the admin types", () => {
    const live = { names: { fa: "a" }, slugs: {}, descriptions: {}, seo: [{ locale: "fa", metaTitle: null, metaDescription: null, tags: null }] };
    expect(applyLiveInput(live, "name-fa", "b").names.fa).toBe("b");
    expect(applyLiveInput(live, "seo-title-fa", "T").seo[0].metaTitle).toBe("T");
    expect(applyLiveInput(live, "sku", "x")).toBe(live);
  });
});

describe("SynonymsEditor", () => {
  it("splits words on Latin or Persian commas and saves a new group", async () => {
    expect(splitTerms("گوشی، موبایل , ,phone\nmobile")).toEqual(["گوشی", "موبایل", "phone", "mobile"]);
    respond(200, { id: "s1", locale: "fa", terms: ["گوشی", "موبایل"] });
    render(<SynonymsEditor texts={texts} list={{ locales: ["fa", "en"], groups: [] }} languageNames={names} />);

    fireEvent.change(screen.getByLabelText(texts.synonymTerms), { target: { value: "گوشی، موبایل" } });
    fireEvent.click(screen.getByRole("button", { name: texts.add }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(bodyOf()).toEqual({ locale: "fa", terms: ["گوشی", "موبایل"] });
  });

  it("shows why a group was refused", async () => {
    respond(409, { status: 409, code: "seo.term_in_other_group" });
    render(<SynonymsEditor texts={texts} list={{ locales: ["fa"], groups: [{ id: "s1", locale: "fa", terms: ["a", "b"] }] }} languageNames={names} />);

    fireEvent.click(screen.getByRole("button", { name: texts.save }));

    await waitFor(() => expect(screen.getByText(texts.seoErrors.term_in_other_group)).toBeTruthy());
    expect(apiCalls()[0][0]).toBe("/api/v1/admin/seo/synonyms/s1");
  });
});

describe("RedirectsEditor", () => {
  const redirects = [{ id: "r1", fromPath: "/old", toPath: "/fa", kind: "Permanent" as const, hitCount: 3, lastHitUtc: null }];

  it("lists redirects with their use and adds a removed (410) address without a target", async () => {
    respond(200, { id: "r2" });
    render(<RedirectsEditor locale="en" texts={texts} redirects={redirects} />);

    expect(screen.getByText("/old")).toBeTruthy();
    expect(screen.getByRole("cell", { name: texts.redirectKinds.Permanent })).toBeTruthy();
    fireEvent.change(screen.getByLabelText(texts.redirectFrom), { target: { value: "/removed" } });
    fireEvent.change(screen.getByLabelText(texts.redirectKind), { target: { value: "Gone" } });
    expect((screen.getByLabelText(texts.redirectTo) as HTMLInputElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: texts.add }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(bodyOf()).toEqual({ fromPath: "/removed", toPath: null, kind: "Gone" });
  });

  it("imports CSV and reports each refused line", async () => {
    respond(200, { added: 1, updated: 0, errors: [{ line: 2, code: "seo.path_invalid" }] });
    render(<RedirectsEditor locale="en" texts={texts} redirects={[]} />);

    fireEvent.change(screen.getByPlaceholderText(/old-page,/), { target: { value: "/a,/b\nbad,/c" } });
    fireEvent.click(screen.getByRole("button", { name: texts.redirectImportButton }));

    await waitFor(() => expect(screen.getByText(texts.redirectImported.replace("{added}", "1").replace("{updated}", "0"), { exact: false })).toBeTruthy());
    expect(screen.getByRole("listitem").textContent).toBe(texts.redirectImportLine.replace("{line}", "2").replace("{error}", texts.seoErrors.path_invalid));
    expect(bodyOf()).toEqual({ text: "/a,/b\nbad,/c" });
  });
});
