import { Icon } from "@/components/ui/Icon";

type Texts = { search: string; searchPlaceholder: string; searchSubmit: string };

type Props = { locale: string; texts: Texts; defaultValue?: string; className?: string };

/** A plain GET form: works without JavaScript, and the result page's URL can be shared or bookmarked. */
export function SearchForm({ locale, texts, defaultValue, className = "" }: Props) {
  return (
    <form role="search" action={`/${locale}/search`} method="get" aria-label={texts.search} className={className}>
      <div className="flex h-11 items-center gap-2 rounded-xl border border-border bg-surface-muted ps-3 text-muted focus-within:border-primary md:h-12">
        <Icon name="search" size={20} />
        <input
          type="search"
          name="q"
          defaultValue={defaultValue}
          aria-label={texts.search}
          placeholder={texts.searchPlaceholder}
          enterKeyHint="search"
          className="h-full min-w-0 flex-1 bg-transparent text-foreground outline-none placeholder:text-muted"
        />
        <button type="submit" className="sr-only">
          {texts.searchSubmit}
        </button>
      </div>
    </form>
  );
}
