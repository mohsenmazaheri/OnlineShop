// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { SearchIndexPanel } from "./SearchIndexPanel";

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

describe("SearchIndexPanel", () => {
  it("shows the index size and queues a rebuild", async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ documents: 110, pending: 1 }), { status: 200 }));
    render(<SearchIndexPanel locale="en" texts={en.admin} status={{ documents: 110, pending: 0 }} />);

    expect(screen.getByText("110 search documents, 0 changes waiting")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: en.admin.searchIndexRebuild }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/admin/seo/search-index/rebuild");
    expect(screen.getByRole("status").textContent).toBe(en.admin.searchIndexQueued);
    expect(screen.queryByText(/Search engine/)).toBeNull();
  });

  it("says whether the search engine answers or the database stands in", () => {
    const { rerender } = render(
      <SearchIndexPanel locale="en" texts={en.admin} status={{ documents: 4, pending: 0, engine: { provider: "OpenSearch", serving: true, pending: 2 } }} />,
    );
    expect(screen.getByText("Search engine: OpenSearch, answering searches. 2 products waiting to reach it.")).toBeTruthy();

    rerender(<SearchIndexPanel locale="en" texts={en.admin} status={{ documents: 4, pending: 0, engine: { provider: "Elasticsearch", serving: false, pending: 0 } }} />);
    expect(screen.getByText(/Elasticsearch is not answering right now/).className).toContain("text-danger");
  });
});
