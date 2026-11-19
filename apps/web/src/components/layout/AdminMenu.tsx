"use client";

import Link from "next/link";
import { useEffect, useRef } from "react";
import { Icon, type IconName } from "@/components/ui/Icon";

export type AdminMenuGroup = { title: string; items: { href: string; label: string; icon: IconName }[] };

type Props = { label: string; homeHref: string; homeLabel: string; groups: AdminMenuGroup[] };

/**
 * The admin panel's sections in the shop header, for staff only. A <details> menu, so it opens without JavaScript;
 * with it, it also closes on a choice, an outside click or Escape.
 */
export function AdminMenu({ label, homeHref, homeLabel, groups }: Props) {
  const menu = useRef<HTMLDetailsElement>(null);

  useEffect(() => {
    const close = (event: Event) => {
      const details = menu.current;
      if (!details?.open) return;
      if (event instanceof KeyboardEvent ? event.key === "Escape" : !details.contains(event.target as Node)) details.open = false;
    };
    document.addEventListener("click", close);
    document.addEventListener("keydown", close);
    return () => {
      document.removeEventListener("click", close);
      document.removeEventListener("keydown", close);
    };
  }, []);

  const closeMenu = () => {
    if (menu.current) menu.current.open = false;
  };

  return (
    <details ref={menu} className="relative">
      <summary
        aria-label={label}
        title={label}
        className="flex h-11 cursor-pointer list-none items-center gap-2 rounded-xl border border-primary px-3 text-sm font-medium text-primary hover:bg-primary/5 [&::-webkit-details-marker]:hidden"
      >
        <Icon name="dashboard" size={20} />
        <span className="hidden whitespace-nowrap xl:inline">{label}</span>
      </summary>
      <div className="absolute end-0 top-full z-40 mt-2 flex w-64 flex-col gap-3 rounded-2xl border border-border bg-surface p-2 text-sm shadow-lg">
        <Link href={homeHref} onClick={closeMenu} className="flex items-center gap-2 rounded-lg px-3 py-2 font-bold hover:bg-background">
          <Icon name="dashboard" size={18} />
          {homeLabel}
        </Link>
        {groups.map((group) => (
          <div key={group.title} className="flex flex-col">
            <p className="px-3 pb-1 text-xs font-bold text-muted">{group.title}</p>
            {group.items.map((item) => (
              <Link key={item.href} href={item.href} onClick={closeMenu} className="flex items-center gap-2 rounded-lg px-3 py-2 hover:bg-background">
                <Icon name={item.icon} size={18} />
                {item.label}
              </Link>
            ))}
          </div>
        ))}
      </div>
    </details>
  );
}
