// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ProductReviews as ReviewsData } from "@/lib/api/storefront";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { push: vi.fn(), refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

import { ReviewModeration } from "@/components/admin/ReviewModeration";
import { Stars } from "@/components/ui/Stars";
import { resetWishlistIds, WishlistButton } from "@/components/wishlist/WishlistButton";
import { WishlistRemove } from "@/components/wishlist/WishlistRemove";
import { ProductReviews } from "./ProductReviews";

const errors = { ...en.account.errors, ...en.reviews.errors };
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  router.push.mockReset();
  router.refresh.mockReset();
  resetWishlistIds();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const reviews: ReviewsData = {
  summary: { average: 4.5, count: 2, stars: [1, 1, 0, 0, 0] },
  items: [
    { id: "r1", authorName: "Sara", rating: 5, title: "Great", body: "Works very well.", locale: "en", verifiedBuyer: true, createdUtc: "2026-11-20T10:00:00Z" },
    { id: "r2", authorName: null, rating: 4, title: null, body: "خوب است.", locale: "fa", verifiedBuyer: false, createdUtc: "2026-11-19T10:00:00Z" },
  ],
  page: 1,
  pageSize: 1,
  total: 2,
};

describe("Stars", () => {
  it("fills to the rating and names it for screen readers", () => {
    render(<Stars rating={3.5} label="3.5 out of 5 stars" />);
    const stars = screen.getByRole("img", { name: "3.5 out of 5 stars" });
    expect((stars.lastElementChild as HTMLElement).style.width).toBe("70%");
  });
});

describe("ProductReviews", () => {
  it("shows the summary, the reviews in their language and real page links", () => {
    render(<ProductReviews reviews={reviews} number={1001} path="/en/p/1001/x1" locale="en" texts={en.reviews} errors={errors} />);

    const section = screen.getByRole("region", { name: /Reviews/ });
    expect(within(section).getByText("4.5")).toBeTruthy();
    expect(within(section).getByText(en.reviews.verified)).toBeTruthy();
    expect(within(section).getByText(en.reviews.anonymous)).toBeTruthy();
    expect(screen.getByText("خوب است.").closest("article")!.getAttribute("lang")).toBe("fa");
    expect(screen.getByRole("link", { name: en.reviews.next }).getAttribute("href")).toBe("/en/p/1001/x1?reviews=2#reviews");
    expect(screen.queryByRole("link", { name: en.reviews.previous })).toBeNull();
  });

  it("sends a guest to sign in, and lets a confirmed customer write a review that waits for approval", async () => {
    fetchMock.mockResolvedValueOnce(json({ signedIn: false, canReview: false, review: null }));
    const { unmount } = render(
      <ProductReviews reviews={{ ...reviews, items: [], summary: { average: 0, count: 0, stars: [0, 0, 0, 0, 0] }, total: 0 }} number={7} path="/en/p/7/x" locale="en" texts={en.reviews} errors={errors} />,
    );
    expect(screen.getByText(en.reviews.none)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: en.reviews.write }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith("/en/account/login?returnUrl=%2Fen%2Fp%2F7%2Fx%23reviews"));
    unmount();

    fetchMock.mockResolvedValueOnce(json({ signedIn: true, canReview: true, review: null }));
    fetchMock.mockResolvedValueOnce(json({ rating: 4, title: null, body: "Very good item indeed.", status: "Pending", writtenUtc: "2026-11-21T10:00:00Z" }));
    render(<ProductReviews reviews={reviews} number={7} path="/en/p/7/x" locale="en" texts={en.reviews} errors={errors} />);
    fireEvent.click(screen.getByRole("button", { name: en.reviews.write }));
    const form = await screen.findByRole("group", { name: en.reviews.yourRating });
    fireEvent.click(within(form).getByLabelText(en.reviews.stars.replace("{rating}", "4")));
    fireEvent.change(screen.getByLabelText(en.reviews.bodyLabel), { target: { value: "Very good item indeed." } });
    fireEvent.click(screen.getByRole("button", { name: en.reviews.submit }));

    expect(await screen.findByText(en.reviews.thanks)).toBeTruthy();
    expect(screen.getByText(en.reviews.pending)).toBeTruthy();
    const [path, init] = fetchMock.mock.calls[2];
    expect(path).toBe("/api/v1/account/reviews/7?locale=en");
    expect(init.method).toBe("PUT");
    expect(JSON.parse(init.body as string)).toEqual({ rating: 4, title: null, body: "Very good item indeed." });
  });

  it("tells an unconfirmed account what to do", async () => {
    fetchMock.mockResolvedValueOnce(json({ signedIn: true, canReview: false, review: null }));
    render(<ProductReviews reviews={reviews} number={7} path="/en/p/7/x" locale="en" texts={en.reviews} errors={errors} />);
    fireEvent.click(screen.getByRole("button", { name: en.reviews.write }));
    expect(await screen.findByText(en.reviews.unverified)).toBeTruthy();
  });
});

describe("WishlistButton", () => {
  it("adds a product for a signed-in customer and sends a guest to sign in", async () => {
    fetchMock.mockResolvedValueOnce(json({ numbers: [], signedIn: true }));
    fetchMock.mockResolvedValueOnce(json({ numbers: [5], signedIn: true }));
    const { unmount } = render(<WishlistButton locale="en" number={5} returnPath="/en/p/5/x" texts={en.product} />);
    const button = await screen.findByRole("button", { name: en.product.wishlistAdd });
    await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(false));
    fireEvent.click(button);
    expect(await screen.findByRole("button", { name: en.product.wishlistAdded })).toBeTruthy();
    expect(fetchMock.mock.calls[1][0]).toBe("/api/v1/account/wishlist/5");
    expect(fetchMock.mock.calls[1][1].method).toBe("PUT");
    unmount();

    resetWishlistIds();
    fetchMock.mockResolvedValueOnce(json({ numbers: [], signedIn: false }));
    render(<WishlistButton locale="en" number={5} returnPath="/en/p/5/x" texts={en.product} />);
    const guest = await screen.findByRole("button", { name: en.product.wishlistAdd });
    await waitFor(() => expect((guest as HTMLButtonElement).disabled).toBe(false));
    fireEvent.click(guest);
    await waitFor(() => expect(router.push).toHaveBeenCalledWith("/en/account/login?returnUrl=%2Fen%2Fp%2F5%2Fx"));
  });
});

describe("WishlistRemove", () => {
  it("takes the product off and reloads the list", async () => {
    fetchMock.mockResolvedValueOnce(json({ numbers: [], signedIn: true }));
    render(<WishlistRemove number={9} label="Remove Orvia X1" />);
    fireEvent.click(screen.getByRole("button", { name: "Remove Orvia X1" }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/account/wishlist/9");
    expect(fetchMock.mock.calls[0][1].method).toBe("DELETE");
  });
});

describe("ReviewModeration", () => {
  it("filters by status and approves a review, clearing the storefront's cached reviews", async () => {
    const list = {
      items: [
        { id: "r1", productNumber: 1001, productName: "Orvia X1", authorName: null, rating: 2, title: "Meh", body: "Not for me.", locale: "en", status: "Pending" as const, verifiedBuyer: true, createdUtc: "2026-11-20T10:00:00Z" },
      ],
      page: 1,
      pageSize: 25,
      total: 1,
      pendingCount: 1,
    };
    fetchMock.mockResolvedValueOnce(json({ ...list.items[0], status: "Approved" }));
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    render(<ReviewModeration locale="en" texts={en.admin} errors={errors} list={list} status="Pending" />);

    expect(screen.getByText(en.admin.reviewsWaiting.replace("{count}", "1"))).toBeTruthy();
    expect(screen.getByRole("link", { name: en.admin.reviewStatuses.Pending }).getAttribute("aria-current")).toBe("page");
    expect(screen.getByRole("link", { name: en.admin.reviewFilterAll }).getAttribute("href")).toBe("/en/admin/reviews");
    fireEvent.click(screen.getByRole("button", { name: en.admin.reviewApprove }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/admin/reviews/r1");
    expect(JSON.parse(fetchMock.mock.calls[0][1].body as string)).toEqual({ status: "Approved" });
    expect(JSON.parse(fetchMock.mock.calls[1][1].body as string)).toEqual({ tags: ["reviews"] });
    expect(screen.queryByRole("button", { name: en.admin.reviewApprove })).toBeNull();
  });
});
