"use client";

import Image from "next/image";
import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { displayMoney } from "@/components/product/Price";
import { Icon } from "@/components/ui/Icon";
import type { components } from "@/lib/api/schema";
import { productPath } from "@/lib/catalog/product-path";
import type { DisplayCurrency } from "@/lib/currency/display";
import { imageVariant } from "@/lib/media/image";

type Suggestions = components["schemas"]["SearchSuggestions"];

type Texts = {
  search: string;
  searchPlaceholder: string;
  searchSubmit: string;
  suggestProducts: string;
  suggestCategories: string;
  suggestBrands: string;
  suggestPopular: string;
  suggestAll: string;
  suggestLabel: string;
};

type Props = { locale: string; texts: Texts; money: DisplayCurrency; defaultValue?: string; className?: string };

type Option = { id: string; href: string; group: keyof Texts; node: React.ReactNode };

/** Waits this long after the last key before asking (typing fast sends one request, not one per key). */
export const debounceMs = 200;

/**
 * The search box with suggestions while typing (from two characters): products with image and price, categories,
 * brands and popular searches. Each new key cancels the previous request. Arrow keys move through the suggestions,
 * Enter opens the chosen one (or searches), Escape closes them. Without JavaScript it is the plain GET form.
 */
export function SearchSuggest({ locale, texts, money, defaultValue = "", className = "" }: Props) {
  const [query, setQuery] = useState(defaultValue);
  const [suggestions, setSuggestions] = useState<Suggestions | null>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const listId = useId();
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const text = query.trim();
    // Under two characters nothing is asked (and the list stays hidden).
    if (text.length < 2) return;
    const controller = new AbortController();
    const timer = setTimeout(async () => {
      try {
        const response = await fetch(`/api/v1/catalog/suggest?q=${encodeURIComponent(text)}&locale=${locale}`, { signal: controller.signal });
        if (response.ok) {
          setSuggestions((await response.json()) as Suggestions);
          setActive(-1);
        }
      } catch {
        // Cancelled by the next key, or offline: the form still searches.
      }
    }, debounceMs);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [query, locale]);

  useEffect(() => {
    const close = (event: MouseEvent) => {
      if (!box.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("click", close);
    return () => document.removeEventListener("click", close);
  }, []);

  const options: Option[] = suggestions
    ? [
        ...suggestions.terms.map((term, i) => ({
          id: `${listId}-t${i}`,
          group: "suggestPopular" as const,
          href: `/${locale}/search?q=${encodeURIComponent(term)}`,
          node: (
            <span className="flex items-center gap-2">
              <Icon name="search" size={16} />
              {term}
            </span>
          ),
        })),
        ...suggestions.products.map((p) => ({
          id: `${listId}-p${p.number}`,
          group: "suggestProducts" as const,
          href: productPath(locale, p.number, p.slug),
          node: (
            <span className="flex items-center gap-3">
              <span className="h-10 w-10 shrink-0 overflow-hidden rounded-lg bg-surface-muted">
                {p.imageUrl && <Image src={imageVariant(p.imageUrl, 300)} alt="" width={40} height={40} unoptimized className="h-full w-full object-cover" />}
              </span>
              <span className="line-clamp-1 flex-1">{p.name}</span>
              <span className="shrink-0 text-xs font-bold">{displayMoney(p.price, money, locale)}</span>
            </span>
          ),
        })),
        ...suggestions.categories.map((c) => ({ id: `${listId}-c${c.path}`, group: "suggestCategories" as const, href: `/${locale}/${c.path}`, node: c.name })),
        ...suggestions.brands.map((b) => ({ id: `${listId}-b${b.path}`, group: "suggestBrands" as const, href: `/${locale}/${b.path}`, node: b.name })),
      ]
    : [];
  const all = { id: `${listId}-all`, group: "suggestAll" as const, href: `/${locale}/search?q=${encodeURIComponent(query.trim())}` };
  const showing = open && query.trim().length >= 2 && options.length > 0;

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") {
      setOpen(false);
      return;
    }
    if (!showing) return;
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      setActive((current) => (current + step + options.length + 1) % (options.length + 1));
    } else if (event.key === "Enter" && active >= 0) {
      event.preventDefault();
      window.location.assign(active < options.length ? options[active].href : all.href);
    }
  }

  const groups = (["suggestPopular", "suggestProducts", "suggestCategories", "suggestBrands"] as const).filter((g) => options.some((o) => o.group === g));
  const activeId = active < 0 ? undefined : active < options.length ? options[active].id : all.id;

  return (
    <form role="search" action={`/${locale}/search`} method="get" aria-label={texts.search} className={className}>
      <div ref={box} className="relative">
        <div className="flex h-11 items-center gap-2 rounded-xl border border-border bg-surface-muted ps-3 text-muted focus-within:border-primary md:h-12">
          <Icon name="search" size={20} />
          <input
            type="search"
            name="q"
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setOpen(true);
            }}
            onFocus={() => setOpen(true)}
            onKeyDown={onKeyDown}
            role="combobox"
            aria-expanded={showing}
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={activeId}
            aria-label={texts.search}
            placeholder={texts.searchPlaceholder}
            enterKeyHint="search"
            autoComplete="off"
            className="h-full min-w-0 flex-1 bg-transparent text-foreground outline-none placeholder:text-muted"
          />
          <button type="submit" className="sr-only">
            {texts.searchSubmit}
          </button>
        </div>
        {showing && (
          <div
            id={listId}
            role="listbox"
            aria-label={texts.suggestLabel}
            className="absolute inset-x-0 top-full z-50 mt-2 max-h-[70vh] overflow-auto rounded-2xl border border-border bg-surface p-2 text-sm text-foreground shadow-lg"
          >
            {groups.map((group) => (
              <div key={group} role="group" aria-label={texts[group]} className="flex flex-col py-1">
                <p className="px-2 pb-1 text-xs font-bold text-muted">{texts[group]}</p>
                {options
                  .filter((o) => o.group === group)
                  .map((o) => (
                    <a
                      key={o.id}
                      id={o.id}
                      href={o.href}
                      role="option"
                      aria-selected={o.id === activeId}
                      className="rounded-lg px-2 py-2 hover:bg-surface-muted aria-selected:bg-primary/10"
                    >
                      {o.node}
                    </a>
                  ))}
              </div>
            ))}
            <a
              id={all.id}
              href={all.href}
              role="option"
              aria-selected={all.id === activeId}
              className="mt-1 block rounded-lg border-t border-border px-2 py-2 font-medium text-primary hover:bg-surface-muted aria-selected:bg-primary/10"
            >
              {texts.suggestAll.replace("{query}", query.trim())}
            </a>
          </div>
        )}
      </div>
    </form>
  );
}
