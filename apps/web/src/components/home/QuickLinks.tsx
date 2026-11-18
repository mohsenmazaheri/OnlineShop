import Link from "next/link";
import { Icon, type IconName } from "@/components/ui/Icon";

export type QuickLink = { href: string; label: string; icon: IconName };

/** A row of shortcut tiles: four round icons on phones, labelled cards from md up. */
export function QuickLinks({ label, links }: { label: string; links: QuickLink[] }) {
  return (
    <nav aria-label={label}>
      <ul className="grid grid-cols-4 gap-2 md:grid-cols-[repeat(auto-fill,minmax(11rem,1fr))] md:gap-3">
        {links.map((link, i) => (
          <li key={link.href}>
            <Link
              href={link.href}
              className="flex flex-col items-center gap-1.5 text-center text-xs text-foreground md:flex-row md:gap-3 md:rounded-2xl md:border md:border-border md:bg-surface md:p-3 md:text-start md:text-sm md:font-medium md:hover:border-primary"
            >
              <span className={`tint tint-${i % 8} flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl md:h-11 md:w-11 md:rounded-xl`}>
                <Icon name={link.icon} size={24} />
              </span>
              {link.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
