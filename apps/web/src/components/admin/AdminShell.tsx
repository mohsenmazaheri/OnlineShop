import Link from "next/link";
import type { ReactNode } from "react";
import { Card } from "@/components/ui/Surface";
import type { Dictionary } from "@/lib/i18n/dictionaries";

export type AdminSection = "home" | "products" | "catalog" | "brands" | "specs" | "staff" | "currencies" | "branding";

type Props = {
  locale: string;
  texts: Dictionary["admin"];
  current: AdminSection;
  /** Sections this user may open (links to the others are not shown). */
  permissions: readonly string[];
  children: ReactNode;
};

/** Frame of every admin page: title, section links the user is allowed to open, and a way back to the shop. */
export function AdminShell({ locale, texts, current, permissions, children }: Props) {
  const items: { section: AdminSection; href: string; label: string; permission?: string }[] = [
    { section: "home", href: `/${locale}/admin`, label: texts.navHome },
    { section: "products", href: `/${locale}/admin/products`, label: texts.navProducts, permission: "catalog.manage" },
    { section: "catalog", href: `/${locale}/admin/catalog`, label: texts.navCatalog, permission: "catalog.manage" },
    { section: "brands", href: `/${locale}/admin/brands`, label: texts.navBrands, permission: "catalog.manage" },
    { section: "specs", href: `/${locale}/admin/specs`, label: texts.navSpecs, permission: "catalog.manage" },
    { section: "staff", href: `/${locale}/admin/staff`, label: texts.navStaff, permission: "staff.manage" },
    { section: "currencies", href: `/${locale}/admin/currencies`, label: texts.navCurrencies, permission: "settings.manage" },
    { section: "branding", href: `/${locale}/admin/branding`, label: texts.navBranding, permission: "settings.manage" },
  ];
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-1 flex-col gap-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-bold">{texts.title}</h1>
        <Link href={`/${locale}`} className="text-sm text-primary underline">
          {texts.backToShop}
        </Link>
      </div>
      <nav aria-label={texts.title} className="flex gap-1 overflow-x-auto rounded-xl bg-background p-1 text-sm">
        {items
          .filter((item) => !item.permission || permissions.includes(item.permission))
          .map((item) => (
            <Link
              key={item.section}
              href={item.href}
              aria-current={item.section === current ? "page" : undefined}
              className={`whitespace-nowrap rounded-lg px-4 py-2 font-medium ${item.section === current ? "bg-surface shadow-sm" : "text-muted hover:text-foreground"}`}
            >
              {item.label}
            </Link>
          ))}
      </nav>
      {children}
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
