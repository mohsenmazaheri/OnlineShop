import Link from "next/link";

/** First letter of the shop name, used as a logo until the shop uploads its own (phase 3d). */
export function monogram(name: string): string {
  const first = Array.from(name.trim())[0] ?? "";
  return first.toLocaleUpperCase();
}

type Props = { locale: string; name: string; inverse?: boolean };

/** Logo mark + shop name, linking to the home page. */
export function Brand({ locale, name, inverse = false }: Props) {
  return (
    <Link href={`/${locale}`} className={`flex min-w-0 items-center gap-2.5 ${inverse ? "text-white" : "text-foreground"}`}>
      <span
        aria-hidden
        className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary text-lg font-extrabold text-primary-foreground md:h-10 md:w-10"
      >
        {monogram(name)}
      </span>
      <span className="truncate text-lg font-extrabold tracking-tight md:text-xl">{name}</span>
    </Link>
  );
}
