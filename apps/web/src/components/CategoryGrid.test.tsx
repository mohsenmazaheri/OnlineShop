// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { CategoryGrid, CategoryGridSkeleton } from "./CategoryGrid";

afterEach(cleanup);

describe("CategoryGrid", () => {
  it("shows a tile per category under the #categories anchor", () => {
    render(
      <CategoryGrid
        locale="en"
        heading="Categories"
        emptyText="None"
        categories={[
          { id: "1", name: "Books", slug: "books", imageUrl: "/demo/categories/books.svg", subCategories: [{ id: "2", name: "Poetry", slug: "poetry" }] },
          { id: "3", name: "Toys", slug: "toys", imageUrl: null, subCategories: [] },
        ]}
      />,
    );

    const section = screen.getByRole("region", { name: "Categories" });
    expect(section.id).toBe("categories");
    // An image where the category has one, otherwise the first letter.
    expect(section.querySelector("img")?.getAttribute("src")).toBe("/demo/categories/books.svg");
    expect(section.querySelectorAll("img")).toHaveLength(1);
    expect(within(section).getByText("T")).toBeTruthy();
    expect(within(section).getByRole("heading", { level: 3, name: "Books" })).toBeTruthy();
    expect(within(section).getByRole("heading", { level: 3, name: "Toys" })).toBeTruthy();
    expect(within(section).getAllByRole("listitem")).toHaveLength(2);
    // Each tile opens the category page in the reader's language.
    expect(within(section).getByRole("link", { name: "Books" }).getAttribute("href")).toBe("/en/c/books");
    expect(screen.queryByText("None")).toBeNull();
  });

  it("shows the empty text when there are no categories", () => {
    render(<CategoryGrid locale="fa" heading="Categories" emptyText="No categories yet." categories={[]} />);

    expect(screen.getByText("No categories yet.")).toBeTruthy();
  });

  it("announces the loading skeleton to screen readers", () => {
    render(<CategoryGridSkeleton label="Loading…" />);

    expect(screen.getByRole("status", { name: "Loading…" })).toBeTruthy();
  });
});
