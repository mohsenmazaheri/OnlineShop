// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));

import { ProductGallery } from "./ProductGallery";

afterEach(cleanup);

const key = (n: number) => String(n).repeat(32);

describe("ProductGallery", () => {
  it("shows the first image large with its alternative text, and switches with the thumbnails", () => {
    render(
      <ProductGallery
        name="Phone"
        label="Product images"
        images={[
          { url: `/api/v1/media/${key(1)}/1200.webp`, alt: "Front" },
          { url: `/api/v1/media/${key(2)}/1200.webp`, alt: null },
        ]}
      />,
    );

    expect(screen.getByRole("img", { name: "Front" }).getAttribute("src")).toBe(`/api/v1/media/${key(1)}/1200.webp`);
    fireEvent.click(screen.getByRole("button", { name: "Phone 2" }));

    // No alternative text: the product name stands in.
    expect(screen.getByRole("img", { name: "Phone" }).getAttribute("src")).toBe(`/api/v1/media/${key(2)}/1200.webp`);
    expect(screen.getByRole("button", { name: "Phone 2" }).getAttribute("aria-current")).toBe("true");
  });

  it("has no thumbnails for a single image and a placeholder for none", () => {
    const { rerender, container } = render(<ProductGallery name="Phone" label="x" images={[{ url: "/demo/products/kettle.svg", alt: null }]} />);
    expect(screen.queryByRole("list")).toBeNull();

    rerender(<ProductGallery name="Phone" label="x" images={[]} />);
    expect(container.querySelector("img")).toBeNull();
  });
});
