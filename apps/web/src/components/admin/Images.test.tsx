// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn(), replace: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));
vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));

import { ImageUploadField } from "./ImageUploadField";
import { ProductImagesEditor, moveImage, type ProductImageView } from "./ProductImagesEditor";

const texts = en.admin;
const key = (n: number) => String(n).repeat(32).slice(0, 32);
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const apiCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).startsWith("/api/v1/"));
const file = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], "photo.png", { type: "image/png" });

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const image = (n: number): ProductImageView => ({
  id: `id-${n}`,
  url: `/api/v1/media/${key(n)}/1200.webp`,
  width: 800,
  height: 600,
  alts: [
    { locale: "fa", alt: "" },
    { locale: "en", alt: "" },
  ],
});

describe("moveImage", () => {
  it("swaps neighbours and ignores moves past the ends", () => {
    expect(moveImage([1, 2, 3], 0, 1)).toEqual([2, 1, 3]);
    expect(moveImage([1, 2, 3], 2, -1)).toEqual([1, 3, 2]);
    expect(moveImage([1, 2, 3], 0, -1)).toEqual([1, 2, 3]);
  });
});

describe("ProductImagesEditor", () => {
  const names = { fa: "فارسی", en: "English" };

  it("uploads files as multipart and shows them with the small size", async () => {
    respond(200, image(2));
    render(<ProductImagesEditor locale="en" number={1001} images={[image(1)]} languageNames={names} texts={texts} />);

    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [file()] } });

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    const [url, init] = apiCalls()[0];
    expect(url).toBe("/api/v1/admin/products/1001/images");
    expect((init as RequestInit).body).toBeInstanceOf(FormData);
    // Thumbnails are decorative (empty alt), so they are found as elements, not by role.
    expect([...document.querySelectorAll("img")].map((i) => i.getAttribute("src"))).toEqual([`/api/v1/media/${key(1)}/300.webp`, `/api/v1/media/${key(2)}/300.webp`]);
  });

  it("saves the new order and alternative texts in one request", async () => {
    respond(200, []);
    render(<ProductImagesEditor locale="en" number={1001} images={[image(1), image(2)]} languageNames={names} texts={texts} />);

    const items = screen.getAllByRole("listitem");
    fireEvent.click(within(items[1]).getByRole("button", { name: texts.imageMoveUp }));
    fireEvent.change(within(screen.getAllByRole("listitem")[0]).getByLabelText("English"), { target: { value: "Side view" } });
    fireEvent.click(screen.getByRole("button", { name: texts.imagesSave }));

    await waitFor(() => expect(screen.getByText(texts.saved)).toBeTruthy());
    const body = JSON.parse(String((apiCalls()[0][1] as RequestInit).body));
    expect(body.images.map((i: { id: string }) => i.id)).toEqual(["id-2", "id-1"]);
    expect(body.images[0].alts).toEqual([{ locale: "fa", alt: "" }, { locale: "en", alt: "Side view" }]);
    expect(within(screen.getAllByRole("listitem")[0]).getByText(texts.imageMain)).toBeTruthy();
  });

  it("shows why an upload was refused", async () => {
    respond(400, { status: 400, code: "media.type_not_allowed" });
    render(<ProductImagesEditor locale="en" number={1001} images={[]} languageNames={names} texts={texts} />);

    fireEvent.change(document.querySelector('input[type="file"]') as HTMLInputElement, { target: { files: [file()] } });

    await waitFor(() => expect(screen.getByText(`photo.png: ${texts.mediaErrors.type_not_allowed}`)).toBeTruthy());
  });
});

describe("ImageUploadField", () => {
  it("uploads a replacement and removes the image", async () => {
    // Upload, cache refresh, delete, cache refresh.
    respond(200, {});
    respond(200, {});
    respond(200, {});
    respond(200, {});
    render(
      <ImageUploadField
        locale="en"
        label={texts.logo}
        currentUrl={`/api/v1/media/${key(1)}/300.webp`}
        uploadPath="/api/v1/admin/branding/Logo"
        removePath="/api/v1/admin/branding/Logo"
        texts={texts}
        errorTexts={{ ...texts.errors, ...texts.mediaErrors }}
        refresh={["store"]}
      />,
    );

    fireEvent.change(document.querySelector('input[type="file"]') as HTMLInputElement, { target: { files: [file()] } });
    await waitFor(() => expect(router.refresh).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole("button", { name: texts.remove }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalledTimes(2));

    expect(apiCalls().map(([url, init]) => [url, (init as RequestInit).method])).toEqual([
      ["/api/v1/admin/branding/Logo", "POST"],
      ["/api/v1/admin/branding/Logo", "DELETE"],
    ]);
    expect(fetchMock.mock.calls.filter(([url]) => url === "/en/admin/refresh")).toHaveLength(2);
  });

  it("says when there is no image yet", () => {
    render(
      <ImageUploadField locale="en" label="x" currentUrl={null} uploadPath="/api/v1/a" removePath="/api/v1/a" texts={texts} errorTexts={texts.errors} refresh={["store"]} />,
    );

    expect(screen.getByText(texts.imageNone)).toBeTruthy();
    expect(screen.queryByRole("button", { name: texts.remove })).toBeNull();
  });
});
