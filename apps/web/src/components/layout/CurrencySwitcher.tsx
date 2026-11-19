"use client";

import { usePathname } from "next/navigation";
import { useRef } from "react";

export type CurrencyOption = { code: string; label: string; short: string };

type Props = { locale: string; current: string; options: CurrencyOption[]; label: string };

/**
 * Lets the visitor show prices in another currency. Each choice is a plain link to /{locale}/currency (it sets a
 * cookie and comes back here), so it also works without JavaScript; the choice holds for this language only.
 */
export function CurrencySwitcher({ locale, current, options, label }: Props) {
  const pathname = usePathname();
  const details = useRef<HTMLDetailsElement>(null);
  if (options.length < 2) return null;

  const currentOption = options.find((o) => o.code === current);
  const returnTo = encodeURIComponent(pathname ?? `/${locale}`);

  return (
    <details ref={details} className="relative">
      <summary
        aria-label={`${label}: ${currentOption?.label ?? current}`}
        title={label}
        className="flex h-10 cursor-pointer list-none items-center rounded-lg px-2 text-sm font-bold text-muted hover:bg-surface-muted hover:text-foreground [&::-webkit-details-marker]:hidden"
      >
        {currentOption?.short ?? current}
      </summary>
      <ul className="absolute end-0 z-40 mt-1 max-h-80 min-w-52 overflow-y-auto rounded-xl border border-border bg-surface p-1 shadow-lg">
        {options.map((option) => (
          <li key={option.code}>
            {/* A full page load: the server sets the cookie and redirects back. */}
            <a
              href={`/${locale}/currency?code=${option.code}&returnTo=${returnTo}`}
              rel="nofollow"
              aria-current={option.code === current ? "true" : undefined}
              onClick={() => details.current?.removeAttribute("open")}
              className="flex min-h-11 items-center justify-between gap-3 rounded-lg px-3 text-sm hover:bg-surface-muted aria-[current]:font-bold aria-[current]:text-primary"
            >
              <span>{option.label}</span>
              <span dir="ltr" className="text-xs text-muted">
                {option.code}
              </span>
            </a>
          </li>
        ))}
      </ul>
    </details>
  );
}
