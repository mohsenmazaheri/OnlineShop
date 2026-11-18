// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("next/link", () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { Button, ButtonLink, buttonClass } from "./Button";
import { Icon, iconNames } from "./Icon";
import { Badge, Card, Skeleton } from "./Surface";

describe("Icon", () => {
  it("draws every icon as a decorative SVG of the requested size", () => {
    for (const name of iconNames) {
      const { container, unmount } = render(<Icon name={name} size={18} />);
      const svg = container.querySelector("svg")!;
      expect(svg.getAttribute("aria-hidden")).toBe("true");
      expect(svg.getAttribute("width")).toBe("18");
      expect(svg.children.length).toBeGreaterThan(0);
      unmount();
    }
  });
});

afterEach(cleanup);

describe("buttons", () => {
  it("never submit a form by accident", () => {
    const onSubmit = vi.fn((e: React.FormEvent) => e.preventDefault());
    render(
      <form onSubmit={onSubmit}>
        <Button>Open</Button>
        <Button type="submit">Save</Button>
      </form>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Open" }));
    expect(onSubmit).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(onSubmit).toHaveBeenCalledOnce();
  });

  it("use the theme tokens for each variant and keep touch-sized defaults", () => {
    expect(buttonClass()).toContain("bg-primary");
    expect(buttonClass()).toContain("h-11");
    expect(buttonClass("danger", "lg")).toContain("bg-danger");
    expect(buttonClass("secondary", "sm", "w-full")).toMatch(/border-border.*h-9.*w-full/);
  });

  it("render links as real links", () => {
    render(<ButtonLink href="/en/admin" variant="ghost">Panel</ButtonLink>);

    const link = screen.getByRole("link", { name: "Panel" });
    expect(link.getAttribute("href")).toBe("/en/admin");
    expect(link.className).toContain("text-primary");
  });
});

describe("surfaces", () => {
  it("cards are labelled by their title", () => {
    render(<Card title="Team members">x</Card>);

    expect(screen.getByRole("region", { name: "Team members" })).toBeTruthy();
  });

  it("badges pick a tone and skeletons stay out of the accessibility tree", () => {
    const { container } = render(
      <>
        <Badge tone="success">Active</Badge>
        <Skeleton className="h-6 w-32" />
      </>,
    );

    expect(screen.getByText("Active").className).toContain("text-success");
    expect(container.querySelector("[aria-hidden=true]")!.className).toContain("h-6 w-32");
  });
});
