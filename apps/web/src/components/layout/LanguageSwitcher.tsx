"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useRef } from "react";
import { Icon } from "@/components/ui/Icon";
import { switchLocalePath } from "./locale-path";

export type LanguageOption = { code: string; name: string };

type Props = { current: string; languages: LanguageOption[]; label: string };

/**
 * Opens a list of the shop's languages; each link is the current page in that language. A native <details>
 * element, so it opens without JavaScript too (the links then point at each language's version of this page as
 * rendered on the server).
 */
export function LanguageSwitcher({ current, languages, label }: Props) {
  const pathname = usePathname();
  const details = useRef<HTMLDetailsElement>(null);
  if (languages.length < 2) return null;

  const codes = languages.map((l) => l.code);
  const currentName = languages.find((l) => l.code === current)?.name ?? current;

  return (
    <details ref={details} className="relative">
      <summary
        aria-label={`${label}: ${currentName}`}
        title={label}
        className="flex h-10 cursor-pointer list-none items-center gap-1.5 rounded-lg px-2 text-sm text-muted hover:bg-surface-muted hover:text-foreground [&::-webkit-details-marker]:hidden"
      >
        <Icon name="globe" size={20} />
        <span className="hidden lg:inline">{currentName}</span>
      </summary>
      <ul className="absolute end-0 z-40 mt-1 min-w-40 rounded-xl border border-border bg-surface p-1 shadow-lg">
        {languages.map((language) => (
          <li key={language.code}>
            <Link
              href={switchLocalePath(pathname, language.code, codes)}
              hrefLang={language.code}
              lang={language.code}
              aria-current={language.code === current ? "true" : undefined}
              onClick={() => details.current?.removeAttribute("open")}
              className="flex min-h-11 items-center rounded-lg px-3 text-sm hover:bg-surface-muted aria-[current]:font-bold aria-[current]:text-primary"
            >
              {language.name}
            </Link>
          </li>
        ))}
      </ul>
    </details>
  );
}
