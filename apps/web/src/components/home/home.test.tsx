// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("next/link", () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { Benefits } from "./Benefits";
import { HeroBanner } from "./HeroBanner";
import { QuickLinks } from "./QuickLinks";

afterEach(cleanup);

describe("HeroBanner", () => {
  it("carries the page heading and a call to action", () => {
    render(<HeroBanner title="Garden" text="Plants and pots" badge="Welcome" cta="Browse" ctaHref="/en#categories" />);

    expect(screen.getByRole("heading", { level: 1, name: "Garden" })).toBeTruthy();
    expect(screen.getByText("Plants and pots")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Browse" }).getAttribute("href")).toBe("/en#categories");
  });
});

describe("QuickLinks", () => {
  it("lists the shortcuts in a labelled navigation", () => {
    render(
      <QuickLinks
        label="Quick access"
        links={[
          { href: "/en/cart", label: "Cart", icon: "cart" },
          { href: "/en/account", label: "Account", icon: "user" },
        ]}
      />,
    );

    const nav = screen.getByRole("navigation", { name: "Quick access" });
    expect(within(nav).getAllByRole("link").map((l) => l.getAttribute("href"))).toEqual(["/en/cart", "/en/account"]);
  });
});

describe("Benefits", () => {
  it("shows every benefit with its text", () => {
    render(
      <Benefits
        label="Why us"
        items={[
          { icon: "lock", title: "Secure payment", text: "Trusted gateways" },
          { icon: "truck", title: "Delivery", text: "To your door" },
        ]}
      />,
    );

    const section = screen.getByRole("region", { name: "Why us" });
    expect(within(section).getAllByRole("listitem")).toHaveLength(2);
    expect(within(section).getByText("Trusted gateways")).toBeTruthy();
  });
});
