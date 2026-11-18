import { formatSpecValue, groupSpecs, type SpecLine, type SpecTexts } from "@/lib/catalog/specs";

type Props = { specs: SpecLine[]; locale: string; texts: SpecTexts; heading: string };

/** All specifications, grouped under their headings, as a description list per group. */
export function SpecTable({ specs, locale, texts, heading }: Props) {
  if (specs.length === 0) return null;
  return (
    <section aria-labelledby="specs-heading" className="flex flex-col gap-4">
      <h2 id="specs-heading" className="text-lg font-extrabold">
        {heading}
      </h2>
      {groupSpecs(specs).map(({ group, lines }) => (
        <div key={group} className="flex flex-col gap-1 md:flex-row md:gap-6">
          {group && <h3 className="text-sm font-bold text-muted md:w-40 md:shrink-0 md:pt-2">{group}</h3>}
          <dl className="flex-1 divide-y divide-border">
            {lines.map((line) => (
              <div key={line.code} className="grid grid-cols-[minmax(7rem,2fr)_3fr] gap-3 py-2 text-sm">
                <dt className="text-muted">{line.name}</dt>
                <dd>{formatSpecValue(line, locale, texts)}</dd>
              </div>
            ))}
          </dl>
        </div>
      ))}
    </section>
  );
}
