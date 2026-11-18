import Link from "next/link";
import { Icon } from "@/components/ui/Icon";

type Props = { title: string; text: string; badge: string; cta: string; ctaHref: string };

/**
 * The large welcome banner at the top of the home page, carrying the page's h1. Until shops can upload banner images
 * (phase 3d with file storage in phase 4) it shows the shop name and description over a drawn pattern.
 */
export function HeroBanner({ title, text, badge, cta, ctaHref }: Props) {
  return (
    <section className="relative flex min-h-44 items-center overflow-hidden rounded-[calc(var(--radius)*1.75)] bg-primary px-5 py-6 text-primary-foreground dark:bg-[color-mix(in_srgb,var(--primary)_28%,var(--surface))] dark:text-foreground md:min-h-80 md:rounded-[calc(var(--radius)*2)] md:px-12 md:py-10">
      <div className="relative z-10 flex max-w-xl flex-col items-start gap-3 md:gap-4">
        <span className="rounded-full bg-accent px-3 py-0.5 text-xs font-bold text-accent-foreground md:text-sm">{badge}</span>
        <h1 className="text-2xl leading-snug font-extrabold md:text-5xl md:leading-tight">{title}</h1>
        <p className="max-w-md text-sm opacity-90 md:text-base">{text}</p>
        <Link
          href={ctaHref}
          className="mt-1 inline-flex h-11 items-center gap-2 rounded-xl bg-surface px-5 font-bold text-foreground hover:opacity-90 md:h-12"
        >
          {cta}
          <Icon name="chevron" size={18} strokeWidth={2} className="rtl:-scale-x-100" />
        </Link>
      </div>
      {/* Decorative shapes only (no image to download, nothing to shift while loading). */}
      <div aria-hidden className="pointer-events-none absolute inset-y-0 end-0 w-1/2 max-md:opacity-50">
        <span className="absolute -end-10 -top-16 h-56 w-56 rounded-full bg-white/10 md:h-80 md:w-80" />
        <span className="absolute end-56 bottom-[-4rem] hidden h-56 w-56 rounded-[2rem] bg-accent/30 md:block" />
        <span className="absolute end-8 bottom-8 hidden h-24 w-24 rounded-full border-2 border-dashed border-white/40 md:block" />
      </div>
    </section>
  );
}
