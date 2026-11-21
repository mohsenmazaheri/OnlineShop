// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { ContentBody, parseBlocks, safeHref } from "./markdown";

afterEach(cleanup);

describe("content marks", () => {
  it("splits a text into headings, lists, quotes and paragraphs", () => {
    expect(parseBlocks("## Title\n\nFirst line\nsecond line\n\n- a\n- b\n\n1. one\n2) two\n\n> said\n> twice\r\n\r\n### Small")).toEqual([
      { kind: "h2", text: "Title" },
      { kind: "p", text: "First line\nsecond line" },
      { kind: "ul", items: ["a", "b"] },
      { kind: "ol", items: ["one", "two"] },
      { kind: "quote", text: "said\ntwice" },
      { kind: "h3", text: "Small" },
    ]);
    expect(parseBlocks("   \n\n")).toEqual([]);
  });

  it("allows only safe link addresses", () => {
    expect(["/fa/p/1001", "#top", "https://example.com", "mailto:a@b.c"].every(safeHref)).toBe(true);
    expect(["javascript:alert(1)", "data:text/html,x", "//evil.example", "vbscript:x"].some(safeHref)).toBe(false);
  });

  it("renders bold, links and line breaks without ever writing HTML from the text", () => {
    const { container } = render(
      <ContentBody source={"## راهنما\n\nThis is **important** and [our phones](/fa/c/phones), not [this](javascript:alert(1)).\nNext line <script>x</script>\n\n- [site](https://example.com)"} />,
    );
    expect(screen.getByRole("heading", { level: 2 }).textContent).toBe("راهنما");
    expect(screen.getByText("important").tagName).toBe("STRONG");
    expect(screen.getByRole("link", { name: "our phones" }).getAttribute("href")).toBe("/fa/c/phones");
    expect(screen.queryByRole("link", { name: "this" })).toBeNull();
    expect(screen.getByRole("link", { name: "site" }).getAttribute("rel")).toBe("noopener");
    expect(container.querySelector("script")).toBeNull();
    expect(container.textContent).toContain("<script>x</script>");
    expect(container.querySelectorAll("br")).toHaveLength(1);
  });
});
