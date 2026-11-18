import { Icon, type IconName } from "@/components/ui/Icon";

export type Benefit = { icon: IconName; title: string; text: string };

/** Why shop here: short promises shown near the end of the home page. */
export function Benefits({ label, items }: { label: string; items: Benefit[] }) {
  return (
    <section aria-label={label}>
      <ul className="grid grid-cols-2 gap-2 md:gap-3 lg:grid-cols-4">
        {items.map((item) => (
          <li key={item.title} className="flex flex-col gap-2 rounded-2xl border border-border bg-surface p-3 sm:flex-row sm:items-center sm:gap-3.5 sm:p-4">
            <span className="shrink-0 text-primary">
              <Icon name={item.icon} size={30} strokeWidth={1.6} />
            </span>
            <span className="flex flex-col">
              <strong className="text-sm sm:text-[15px]">{item.title}</strong>
              <span className="text-xs text-muted sm:text-sm">{item.text}</span>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
