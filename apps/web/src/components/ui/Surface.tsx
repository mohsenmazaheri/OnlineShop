import type { HTMLAttributes, ReactNode } from "react";

/** A white (or dark) panel with border and soft shadow, optionally titled. */
export function Card({ title, titleId, className, children, ...rest }: HTMLAttributes<HTMLElement> & { title?: string; titleId?: string; children: ReactNode }) {
  const id = titleId ?? (title ? `card-${title.replace(/\s+/g, "-").toLowerCase()}` : undefined);
  return (
    <section aria-labelledby={title ? id : undefined} className={`rounded-2xl border border-border bg-surface p-4 shadow-sm ${className ?? ""}`} {...rest}>
      {title && (
        <h2 id={id} className="mb-3 font-semibold">
          {title}
        </h2>
      )}
      {children}
    </section>
  );
}

export type BadgeTone = "neutral" | "primary" | "success" | "warning" | "danger";

const tones: Record<BadgeTone, string> = {
  neutral: "bg-surface-muted text-muted",
  primary: "bg-primary/10 text-primary",
  success: "bg-success/10 text-success",
  warning: "bg-warning/10 text-warning",
  danger: "bg-danger/10 text-danger",
};

/** A small rounded label (status, count, "default"). */
export function Badge({ tone = "neutral", children }: { tone?: BadgeTone; children: ReactNode }) {
  return <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${tones[tone]}`}>{children}</span>;
}

/**
 * Placeholder with the final element's size while content loads, so nothing jumps when it arrives (no CLS).
 * Purely decorative: screen readers get the surrounding status text instead.
 */
export function Skeleton({ className }: { className: string }) {
  return <span aria-hidden="true" className={`block animate-pulse rounded-lg bg-surface-muted ${className}`} />;
}
