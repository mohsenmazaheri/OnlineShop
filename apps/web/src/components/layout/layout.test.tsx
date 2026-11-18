// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { nav } = vi.hoisted(() => ({ nav: { pathname: "/en" } }));
vi.mock("next/navigation", () => ({ usePathname: () => nav.pathname }));
vi.mock("next/link", () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { BottomNav } from "./BottomNav";
import { Brand, monogram } from "./Brand";
import { LanguageSwitcher } from "./LanguageSwitcher";
import { SearchForm } from "./SearchForm";
import { SiteFooter } from "./SiteFooter";

afterEach(cleanup);

describe("Brand", () => {
  it("links the shop name to the home page with its first letter as the mark", () => {
    render(<Brand locale="en" name="garden shop" />);

    const link = screen.getByRole("link", { name: /garden shop/ });
    expect(link.getAttribute("href")).toBe("/en");
    expect(link.textContent).toContain("G");
  });

  it("takes the first character, also for Persian and emoji names", () => {
    expect(monogram("  آنلاین‌شاپ")).toBe("آ");
    expect(monogram("🌿 Garden")).toBe("🌿");
    expect(monogram("")).toBe("");
  });
});

describe("SearchForm", () => {
  it("is a GET search form for the search page of the locale", () => {
    render(<SearchForm locale="en" texts={en.header} defaultValue="lamp" />);

    const form = screen.getByRole("search", { name: en.header.search });
    expect(form.getAttribute("action")).toBe("/en/search");
    expect(form.getAttribute("method")).toBe("get");
    const input = within(form).getByRole("searchbox", { name: en.header.search }) as HTMLInputElement;
    expect(input.name).toBe("q");
    expect(input.value).toBe("lamp");
  });
});

describe("LanguageSwitcher", () => {
  const languages = [
    { code: "en", name: "English" },
    { code: "fa", name: "فارسی" },
  ];

  it("links to the current page in every language", () => {
    nav.pathname = "/en/account/addresses";
    render(<LanguageSwitcher current="en" languages={languages} label="Language" />);

    const persian = screen.getByRole("link", { name: "فارسی" });
    expect(persian.getAttribute("href")).toBe("/fa/account/addresses");
    expect(persian.getAttribute("hreflang")).toBe("fa");
    expect(screen.getByRole("link", { name: "English" }).getAttribute("aria-current")).toBe("true");
  });

  it("closes after a language is picked", () => {
    const { container } = render(<LanguageSwitcher current="en" languages={languages} label="Language" />);
    const details = container.querySelector("details")!;
    details.setAttribute("open", "");
    // jsdom cannot follow links; the menu closing is what this test is about.
    window.addEventListener("click", (e) => e.preventDefault(), { once: true });

    fireEvent.click(screen.getByRole("link", { name: "فارسی" }));

    expect(details.hasAttribute("open")).toBe(false);
  });

  it("is hidden when the shop has one language", () => {
    const { container } = render(<LanguageSwitcher current="en" languages={[languages[0]]} label="Language" />);

    expect(container.innerHTML).toBe("");
  });
});

describe("BottomNav", () => {
  it("shows the four tabs and marks the current one", () => {
    nav.pathname = "/en/cart";
    render(<BottomNav locale="en" texts={en.header} />);

    const bar = screen.getByRole("navigation", { name: en.header.bottomMenu });
    const links = within(bar).getAllByRole("link");
    expect(links.map((l) => l.getAttribute("href"))).toEqual(["/en", "/en#categories", "/en/cart", "/en/account"]);
    expect(within(bar).getByRole("link", { name: en.header.cart }).getAttribute("aria-current")).toBe("page");
    expect(within(bar).getByRole("link", { name: en.header.home }).getAttribute("aria-current")).toBeNull();
  });

  it("stays out of the admin panel", () => {
    nav.pathname = "/en/admin/staff";
    const { container } = render(<BottomNav locale="en" texts={en.header} />);

    expect(container.innerHTML).toBe("");
  });
});

describe("SiteFooter", () => {
  it("shows the shop, its links, the languages and the copyright line", () => {
    render(
      <SiteFooter locale="en" storeName="Garden" locales={["en", "de"]} description="Plants and pots" texts={en.footer} year="2026" />,
    );

    expect(screen.getByText("Plants and pots")).toBeTruthy();
    expect(screen.getByRole("link", { name: en.footer.addresses }).getAttribute("href")).toBe("/en/account/addresses");
    const languages = screen.getByRole("navigation", { name: en.footer.languages });
    expect(within(languages).getByRole("link", { name: "Deutsch" }).getAttribute("href")).toBe("/de");
    expect(screen.getByText(/© 2026 Garden/)).toBeTruthy();
  });

  it("leaves out the language list for a one-language shop", () => {
    render(<SiteFooter locale="en" storeName="Garden" locales={["en"]} description="" texts={en.footer} year="2026" />);

    expect(screen.queryByRole("navigation", { name: en.footer.languages })).toBeNull();
  });
});
