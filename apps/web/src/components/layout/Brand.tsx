import Image from "next/image";
import Link from "next/link";

/** First letter of the shop name, used as a logo until the shop uploads its own. */
export function monogram(name: string): string {
  const first = Array.from(name.trim())[0] ?? "";
  return first.toLocaleUpperCase();
}

type Props = { locale: string; name: string; inverse?: boolean; logoUrl?: string | null };

/** Logo (uploaded, or the name's first letter) + shop name, linking to the home page. */
export function Brand({ locale, name, inverse = false, logoUrl }: Props) {
  return (
    <Link href={`/${locale}`} className={`flex min-w-0 items-center gap-2.5 ${inverse ? "text-white" : "text-foreground"}`}>
      {logoUrl ? (
        // Fixed box, so the header never shifts while the logo loads; the name next to it is the accessible label.
        <span aria-hidden className="flex h-9 w-9 shrink-0 items-center justify-center overflow-hidden rounded-xl bg-white md:h-10 md:w-10">
          <Image src={logoUrl} alt="" width={40} height={40} unoptimized className="h-full w-full object-contain" />
        </span>
      ) : (
        <span
          aria-hidden
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary text-lg font-extrabold text-primary-foreground md:h-10 md:w-10"
        >
          {monogram(name)}
        </span>
      )}
      <span className="truncate text-lg font-extrabold tracking-tight md:text-xl">{name}</span>
    </Link>
  );
}
