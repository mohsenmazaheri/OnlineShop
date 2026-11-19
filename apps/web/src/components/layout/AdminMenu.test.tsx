// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { AdminMenu } from "./AdminMenu";

afterEach(cleanup);

const groups = [
  { title: "Catalog", items: [{ href: "/en/admin/products", label: "Products", icon: "box" as const }] },
  { title: "Team", items: [{ href: "/en/admin/staff", label: "Staff", icon: "users" as const }] },
];

describe("AdminMenu", () => {
  it("lists the admin home and the sections by group, and closes on a choice, Escape or an outside click", () => {
    const { container } = render(<AdminMenu label="Admin panel" homeHref="/en/admin" homeLabel="Overview" groups={groups} />);
    const details = container.querySelector("details")!;

    expect(screen.getByRole("link", { name: "Overview" }).getAttribute("href")).toBe("/en/admin");
    expect(screen.getByRole("link", { name: "Staff" }).getAttribute("href")).toBe("/en/admin/staff");
    expect(screen.getByText("Catalog")).toBeTruthy();

    details.open = true;
    fireEvent.click(screen.getByRole("link", { name: "Products" }));
    expect(details.open).toBe(false);

    details.open = true;
    fireEvent.keyDown(document, { key: "Escape" });
    expect(details.open).toBe(false);

    details.open = true;
    fireEvent.click(document.body);
    expect(details.open).toBe(false);
  });
});
