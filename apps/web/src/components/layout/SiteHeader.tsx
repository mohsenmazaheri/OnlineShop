import Link from "next/link";
import { Suspense } from "react";
import { ThemeToggle } from "@/components/ThemeToggle";
import { Icon } from "@/components/ui/Icon";
import { getCurrentUser } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { loadDisplayCurrency } from "@/lib/currency/load";
import { currencyName, nativeLanguageName } from "@/lib/i18n/display-names";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";
import { Brand } from "./Brand";
import { CurrencySwitcher } from "./CurrencySwitcher";
import { LanguageSwitcher } from "./LanguageSwitcher";
import { SearchForm } from "./SearchForm";

type Props = { locale: Locale; texts: Dictionary["header"] };

const accountClass =
  "flex h-11 max-w-48 items-center gap-2 rounded-xl border border-border px-4 text-sm font-medium text-foreground hover:border-primary";

/** Per-visitor link: reads the session, so it streams separately and never ends up in a shared cache. */
async function AccountLink({ locale, texts }: Props) {
  const user = await getCurrentUser();
  return user ? (
    <Link href={`/${locale}/account`} className={accountClass}>
      <Icon name="user" size={20} />
      <span className="truncate">{user.displayName || texts.myAccount}</span>
    </Link>
  ) : (
    <Link href={`/${locale}/account/login`} className={accountClass}>
      <Icon name="user" size={20} />
      <span className="truncate">{texts.signIn}</span>
    </Link>
  );
}

async function StoreBrand({ locale }: { locale: Locale }) {
  const store = await loadStore();
  return <Brand locale={locale} name={store.name} logoUrl={store.branding.logoUrl} />;
}

async function StoreLanguages({ locale, label }: { locale: Locale; label: string }) {
  const store = await loadStore();
  const languages = store.locales.map((code) => ({ code, name: nativeLanguageName(code) }));
  return <LanguageSwitcher current={locale} languages={languages} label={label} />;
}

/** The currencies the shop offers, named in the reader's language (or by their shop-defined symbol); current one marked. */
async function StoreCurrencies({ locale, label }: { locale: Locale; label: string }) {
  const store = await loadStore();
  const { money, settings } = await loadDisplayCurrency(locale, store.currency);
  const options = settings.currencies.map((c) => {
    const name = currencyName(c.code, locale);
    return { code: c.code, label: name === c.code && c.symbol ? c.symbol : name, short: c.symbol ?? c.code };
  });
  return <CurrencySwitcher locale={locale} current={money.code} options={options} label={label} />;
}

/**
 * Sticky header. Phones: brand and quick buttons, then a full-width search box (cart and account live in the bottom
 * bar). From md up: brand, search, account and cart in one row, then the main menu.
 * Fixed heights and same-size placeholders keep the layout still while the per-request parts stream in (no CLS).
 */
export function SiteHeader({ locale, texts }: Props) {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface">
      <div className="mx-auto max-w-7xl px-4 md:px-6">
        <div className="flex h-14 items-center gap-3 md:h-[4.5rem] md:gap-6">
          <Suspense fallback={<span className="h-9 w-36 animate-pulse rounded-xl bg-border" />}>
            <StoreBrand locale={locale} />
          </Suspense>
          <SearchForm locale={locale} texts={texts} className="hidden max-w-2xl flex-1 md:block" />
          <div className="ms-auto flex items-center gap-1 md:gap-2">
            <ThemeToggle texts={texts} />
            <Suspense fallback={<span className="h-10 w-10" />}>
              <StoreLanguages locale={locale} label={texts.language} />
            </Suspense>
            <Suspense fallback={<span className="h-10 w-10" />}>
              <StoreCurrencies locale={locale} label={texts.currency} />
            </Suspense>
            <div className="hidden md:block">
              <Suspense fallback={<span className="block h-11 w-36 animate-pulse rounded-xl bg-border" />}>
                <AccountLink locale={locale} texts={texts} />
              </Suspense>
            </div>
            <span aria-hidden className="hidden h-7 w-px bg-border md:block" />
            <Link
              href={`/${locale}/cart`}
              aria-label={texts.cart}
              title={texts.cart}
              className="hidden h-11 w-11 items-center justify-center rounded-xl text-foreground hover:bg-surface-muted md:flex"
            >
              <Icon name="cart" size={24} />
            </Link>
          </div>
        </div>
        <SearchForm locale={locale} texts={texts} className="pb-3 md:hidden" />
        <nav aria-label={texts.mainMenu} className="hidden h-11 items-center gap-1 text-sm md:flex">
          <Link
            href={`/${locale}#categories`}
            className="flex h-11 items-center gap-2 border-b-2 border-primary px-3 font-bold text-foreground"
          >
            <Icon name="menu" size={20} />
            {texts.categories}
          </Link>
        </nav>
      </div>
    </header>
  );
}
