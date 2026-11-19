import Link from "next/link";
import type { ReactNode } from "react";
import { Icon } from "@/components/ui/Icon";
import { Card } from "@/components/ui/Surface";
import { adminHref, sectionsByGroup, type AdminSection } from "@/lib/admin/sections";
import type { Dictionary } from "@/lib/i18n/dictionaries";

export type { AdminSection } from "@/lib/admin/sections";

type Props = {
  locale: string;
  texts: Dictionary["admin"];
  current: AdminSection;
  /** Sections this user may open (links to the others are not shown). */
  permissions: readonly string[];
  children: ReactNode;
};

function NavLink({ href, label, icon, active, compact }: { href: string; label: string; icon: Parameters<typeof Icon>[0]["name"]; active: boolean; compact?: boolean }) {
  return (
    <Link
      href={href}
      aria-current={active ? "page" : undefined}
      className={`flex items-center gap-2 rounded-lg font-medium whitespace-nowrap ${compact ? "px-3 py-2" : "px-3 py-2.5"} ${
        active ? "bg-primary/10 text-primary" : "text-muted hover:bg-background hover:text-foreground"
      }`}
    >
      <Icon name={icon} size={18} />
      {label}
    </Link>
  );
}

/**
 * Frame of every admin page: the sections this user may open, grouped (a side menu on large screens, a scrolling bar
 * on phones), the page content, and a way back to the shop.
 */
export function AdminShell({ locale, texts, current, permissions, children }: Props) {
  const groups = sectionsByGroup(permissions);
  const home = { href: adminHref(locale), label: texts.navHome, icon: "dashboard" as const, active: current === "home" };

  return (
    <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col gap-4 px-4 py-6 md:px-6">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-bold">{texts.title}</h1>
        <Link href={`/${locale}`} className="text-sm text-primary underline">
          {texts.backToShop}
        </Link>
      </div>

      <div className="flex flex-col gap-4 lg:grid lg:grid-cols-[15rem_minmax(0,1fr)] lg:items-start">
        {/* Phones and tablets: every section in one scrolling row. */}
        <nav aria-label={texts.title} className="flex gap-1 overflow-x-auto rounded-xl bg-background p-1 text-sm lg:hidden">
          <NavLink {...home} compact />
          {groups.flatMap(({ sections }) =>
            sections.map((s) => (
              <NavLink key={s.section} href={adminHref(locale, s.path)} label={texts[s.label as keyof typeof texts] as string} icon={s.icon} active={s.section === current} compact />
            )),
          )}
        </nav>

        {/* Large screens: a side menu with a heading per group. */}
        <nav aria-label={texts.title} className="sticky top-24 hidden flex-col gap-4 rounded-2xl border border-border bg-surface p-3 text-sm lg:flex">
          <NavLink {...home} />
          {groups.map(({ group, sections }) => (
            <div key={group} className="flex flex-col gap-0.5">
              <p className="px-3 pb-1 text-xs font-bold text-muted">{texts.groups[group]}</p>
              {sections.map((s) => (
                <NavLink key={s.section} href={adminHref(locale, s.path)} label={texts[s.label as keyof typeof texts] as string} icon={s.icon} active={s.section === current} />
              ))}
            </div>
          ))}
        </nav>

        <div className="flex min-w-0 flex-col gap-4">{children}</div>
      </div>
    </main>
  );
}

/** A titled white card used to group content on admin pages. */
export function AdminCard({ title, id, children }: { title: string; id: string; children: ReactNode }) {
  return (
    <Card title={title} titleId={id}>
      {children}
    </Card>
  );
}
