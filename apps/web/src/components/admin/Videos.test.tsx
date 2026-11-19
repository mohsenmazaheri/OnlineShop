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

import { ProductVideosEditor, formatDuration, formatSize, type ProductVideoView } from "./ProductVideosEditor";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const apiCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).startsWith("/api/v1/"));
const names = { fa: "فارسی", en: "English" };

/** A stand-in XMLHttpRequest that reports progress and answers with the given status and body. */
function fakeXhr(status: number, body: unknown) {
  const sent: { url?: string; body?: FormData } = {};
  class FakeXhr {
    status = status;
    responseText = JSON.stringify(body);
    withCredentials = false;
    upload: { onprogress: ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null } = { onprogress: null };
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    open(_method: string, url: string) {
      sent.url = url;
    }
    setRequestHeader() {}
    send(data: FormData) {
      sent.body = data;
      this.upload.onprogress?.({ lengthComputable: true, loaded: 5, total: 10 });
      queueMicrotask(() => this.onload?.());
    }
  }
  vi.stubGlobal("XMLHttpRequest", FakeXhr);
  return sent;
}

const video = (n: number): ProductVideoView => ({
  id: `v${n}`,
  url: `/api/v1/media/${String(n).repeat(32)}/video.mp4`,
  posterUrl: null,
  sizeBytes: 12_400_000,
  durationSeconds: 65,
  titles: [
    { locale: "fa", title: "" },
    { locale: "en", title: "" },
  ],
});

beforeEach(() => {
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("video helpers", () => {
  it("formats durations and sizes", () => {
    expect(formatDuration(65)).toBe("1:05");
    expect(formatDuration(3723)).toBe("1:02:03");
    expect(formatDuration(null)).toBe("");
    expect(formatSize(12_400_000, "en")).toBe("12.4 MB");
  });
});

describe("ProductVideosEditor", () => {
  it("uploads a video with progress and lists it", async () => {
    const sent = fakeXhr(200, video(2));
    respond(200, {});
    render(<ProductVideosEditor locale="en" number={1001} videos={[]} languageNames={names} texts={texts} />);

    expect(screen.getByText(texts.videoNone)).toBeTruthy();
    const file = new File([new Uint8Array([0, 0, 0, 24])], "tour.mp4", { type: "video/mp4" });
    fireEvent.change(screen.getByLabelText(texts.videoUpload), { target: { files: [file] } });

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(sent.url).toBe("/api/v1/admin/products/1001/videos");
    expect(sent.body?.get("file")).toBeInstanceOf(File);
    expect(document.querySelector("video")?.getAttribute("src")).toBe(video(2).url);
    expect(screen.getByText("1:05 · 12.4 MB")).toBeTruthy();
  });

  it("shows why a video was refused", async () => {
    fakeXhr(400, { status: 400, code: "media.invalid_video" });
    render(<ProductVideosEditor locale="en" number={1001} videos={[]} languageNames={names} texts={texts} />);

    fireEvent.change(screen.getByLabelText(texts.videoUpload), { target: { files: [new File(["x"], "a.mov")] } });

    await waitFor(() => expect(screen.getByText(texts.mediaErrors.invalid_video)).toBeTruthy());
  });

  it("saves the order and titles and stops offering uploads at three videos", async () => {
    respond(200, []);
    respond(200, {});
    render(<ProductVideosEditor locale="en" number={1001} videos={[video(1), video(2), video(3)]} languageNames={names} texts={texts} />);

    expect(screen.queryByLabelText(texts.videoUpload)).toBeNull();
    const items = screen.getAllByRole("listitem");
    fireEvent.click(within(items[1]).getByRole("button", { name: texts.imageMoveUp }));
    fireEvent.change(within(screen.getAllByRole("listitem")[0]).getByLabelText(`${texts.videoTitle} (English)`), { target: { value: "Tour" } });
    fireEvent.click(screen.getByRole("button", { name: texts.videosSave }));

    await waitFor(() => expect(screen.getByText(texts.saved)).toBeTruthy());
    const body = JSON.parse(String((apiCalls()[0][1] as RequestInit).body));
    expect(body.videos.map((v: { id: string }) => v.id)).toEqual(["v2", "v1", "v3"]);
    expect(body.videos[0].titles[1]).toEqual({ locale: "en", title: "Tour" });
  });
});
