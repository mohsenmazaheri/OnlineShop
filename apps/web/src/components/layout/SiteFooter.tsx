import Link from "next/link";
import type { Locale } from "@/lib/i18n/config";
import { nativeLanguageName } from "@/lib/i18n/display-names";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { Brand } from "./Brand";
import { InstallPrompt } from "./InstallPrompt";

type Props = {
  locale: Locale;
  storeName: string;
  locales: readonly Locale[];
  description: string;
  texts: Dictionary["footer"];
  /** The current year, formatted by the caller (Persian calendar and digits for fa). */
  year: string;
  logoUrl?: string | null;
  /** Content pages the admin listed in the footer ("about", "returns"). */
  pages?: readonly { title: string; slug: string }[];
};

const linkClass = "flex min-h-9 items-center text-inverse-foreground hover:text-white";

/** The dark band at the bottom of every page: about the shop, the main links, languages and the copyright line. */
export function SiteFooter({ locale, storeName, locales, description, texts, year, logoUrl, pages = [] }: Props) {
  const shopping = [
    { href: `/${locale}#categories`, label: texts.categories },
    { href: `/${locale}/search`, label: texts.search },
    { href: `/${locale}/cart`, label: texts.cart },
  ];
  const account = [
    { href: `/${locale}/account`, label: texts.myAccount },
    { href: `/${locale}/account/addresses`, label: texts.addresses },
    { href: `/${locale}/account/privacy`, label: texts.privacy },
  ];

  const about = [{ href: `/${locale}/blog`, label: texts.blog }, ...pages.map((p) => ({ href: `/${locale}/${encodeURIComponent(p.slug)}`, label: p.title }))];

  return (
    <footer className="mt-12 bg-inverse text-sm text-inverse-foreground">
      <div className="mx-auto flex max-w-7xl flex-col gap-8 px-4 pt-10 pb-6 md:px-6">
        <div className="flex flex-wrap gap-x-8 gap-y-8 md:gap-x-12">
          <div className="flex min-w-64 flex-[2] flex-col gap-3">
            <Brand locale={locale} name={storeName} inverse logoUrl={logoUrl} />
            <p className="max-w-sm">{description}</p>
            <div className="max-w-sm">
              <InstallPrompt texts={texts} />
            </div>
          </div>
          <FooterLinks title={texts.shopping} links={shopping} />
          <FooterLinks title={texts.account} links={account} />
          <FooterLinks title={texts.aboutShop} links={about} />
        </div>
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-inverse-border pt-5 text-xs">
          <span>
            © {year} {storeName} — {texts.rights}
          </span>
          {locales.length > 1 && (
            <nav aria-label={texts.languages}>
              <ul className="flex flex-wrap gap-x-3 gap-y-1">
                {locales.map((code) => (
                  <li key={code}>
                    <Link
                      href={`/${code}`}
                      hrefLang={code}
                      lang={code}
                      aria-current={code === locale ? "true" : undefined}
                      className="inline-flex min-h-9 items-center hover:text-white aria-[current]:font-bold aria-[current]:text-white"
                    >
                      {nativeLanguageName(code)}
                    </Link>
                  </li>
                ))}
              </ul>
            </nav>
          )}
        </div>
      </div>
    </footer>
  );
}

function FooterLinks({ title, links }: { title: string; links: { href: string; label: string }[] }) {
  return (
    <nav aria-label={title} className="flex min-w-36 flex-1 flex-col gap-1">
      <h2 className="mb-1 text-base font-bold text-white">{title}</h2>
      <ul>
        {links.map((link) => (
          <li key={link.href}>
            <Link href={link.href} className={linkClass}>
              {link.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
