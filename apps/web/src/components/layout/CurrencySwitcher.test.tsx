// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("next/navigation", () => ({ usePathname: () => "/fa/p/1001/گوشی" }));

import { CurrencySwitcher } from "./CurrencySwitcher";

afterEach(cleanup);

const options = [
  { code: "IRR", label: "ریال ایران", short: "IRR" },
  { code: "IRT", label: "تومان", short: "تومان" },
  { code: "USD", label: "دلار آمریکا", short: "USD" },
];

describe("CurrencySwitcher", () => {
  it("links each currency to the choice route with the current page to come back to", () => {
    render(<CurrencySwitcher locale="fa" current="IRT" options={options} label="واحد پول" />);

    const usd = screen.getByRole("link", { name: /دلار آمریکا/ });
    expect(usd.getAttribute("href")).toBe(`/fa/currency?code=USD&returnTo=${encodeURIComponent("/fa/p/1001/گوشی")}`);
    expect(usd.getAttribute("rel")).toBe("nofollow");
    expect(screen.getByRole("link", { name: /تومان/ }).getAttribute("aria-current")).toBe("true");
    expect(screen.getByLabelText("واحد پول: تومان")).toBeTruthy();
  });

  it("is hidden when the shop offers one currency", () => {
    const { container } = render(<CurrencySwitcher locale="fa" current="IRR" options={[options[0]]} label="x" />);

    expect(container.innerHTML).toBe("");
  });
});
