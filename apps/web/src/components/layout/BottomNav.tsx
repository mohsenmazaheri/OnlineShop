"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { CartBadge } from "@/components/cart/CartBadge";
import { Icon, type IconName } from "@/components/ui/Icon";
import { activeTab, type NavTab } from "./locale-path";

type Texts = { bottomMenu: string; home: string; categoriesShort: string; cart: string; cartCount: string; myAccount: string };

/**
 * The phone's tab bar (hidden from md up, where the header shows the same links), marking the current tab. Not shown
 * in the admin panel. Reads the URL, so the layout renders it inside <Suspense> with <BottomNavBar> as the fallback.
 */
export function BottomNav({ locale, texts }: { locale: string; texts: Texts }) {
  const pathname = usePathname();
  if (pathname?.startsWith(`/${locale}/admin`)) return null;
  return <BottomNavBar locale={locale} texts={texts} active={activeTab(pathname, locale)} />;
}

/** The tab bar itself, for a known current tab (null: none marked, e.g. while the URL is not known yet). */
export function BottomNavBar({ locale, texts, active }: { locale: string; texts: Texts; active: NavTab }) {
  const tabs: { tab: Exclude<NavTab, null>; href: string; icon: IconName; label: string }[] = [
    { tab: "home", href: `/${locale}`, icon: "home", label: texts.home },
    { tab: "categories", href: `/${locale}#categories`, icon: "grid", label: texts.categoriesShort },
    { tab: "cart", href: `/${locale}/cart`, icon: "cart", label: texts.cart },
    { tab: "account", href: `/${locale}/account`, icon: "user", label: texts.myAccount },
  ];

  return (
    <nav
      aria-label={texts.bottomMenu}
      className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-surface pb-[env(safe-area-inset-bottom)] md:hidden"
    >
      <ul className="grid grid-cols-4 px-2 pt-1">
        {tabs.map(({ tab, href, icon, label }) => (
          <li key={tab}>
            <Link
              href={href}
              aria-current={active === tab ? "page" : undefined}
              className="flex min-h-14 flex-col items-center justify-center gap-0.5 text-[11px] text-muted aria-[current=page]:font-bold aria-[current=page]:text-primary"
            >
              <span className="relative">
                <Icon name={icon} size={24} />
                {tab === "cart" && <CartBadge locale={locale} label={texts.cartCount} />}
              </span>
              {label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
