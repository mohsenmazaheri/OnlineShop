import { Icon } from "./Icon";

/**
 * Five stars filled to the rating (0–5, fractions shown as a part-filled star). Decorative: the label carries the
 * rating for screen readers ("4.5 out of 5 stars"), or pass label={null} when the text next to it already says it.
 */
export function Stars({ rating, size = 16, label }: { rating: number; size?: number; label: string | null }) {
  const share = `${Math.max(0, Math.min(5, rating)) * 20}%`;
  return (
    <span role={label ? "img" : undefined} aria-label={label ?? undefined} aria-hidden={label ? undefined : true} className="relative inline-flex shrink-0 align-middle">
      <span className="flex text-border">
        {[0, 1, 2, 3, 4].map((i) => (
          <Icon key={i} name="star" size={size} fill="currentColor" strokeWidth={0} />
        ))}
      </span>
      <span className="absolute inset-y-0 start-0 flex overflow-hidden text-accent" style={{ width: share }}>
        {[0, 1, 2, 3, 4].map((i) => (
          <Icon key={i} name="star" size={size} fill="currentColor" strokeWidth={0} className="shrink-0" />
        ))}
      </span>
    </span>
  );
}
