"use client";

import { useEffect, useState } from "react";
import { remaining } from "@/lib/catalog/sale";

export type CountdownTexts = { offerEndsIn: string; offerEnded: string; offerDays: string };

/**
 * "Ends in 2 days 05:12:09" for a special offer, ticking every second. The server (and a browser without JavaScript)
 * shows only the label with the end time in a <time> element: the clock needs the visitor's current time.
 */
export function Countdown({ endsUtc, locale, texts, className = "" }: { endsUtc: string; locale: string; texts: CountdownTexts; className?: string }) {
  const [now, setNow] = useState<number | null>(null);

  useEffect(() => {
    const tick = () => setNow(Date.now());
    const first = setTimeout(tick, 0);
    const timer = setInterval(tick, 1000);
    return () => {
      clearTimeout(first);
      clearInterval(timer);
    };
  }, []);

  const left = now === null ? undefined : remaining(endsUtc, now);
  if (left === null) return <span className={`text-xs text-muted ${className}`}>{texts.offerEnded}</span>;
  const two = new Intl.NumberFormat(locale, { minimumIntegerDigits: 2, useGrouping: false });
  return (
    <span role="timer" aria-live="off" className={`inline-flex flex-wrap items-center gap-1 text-xs font-bold ${className}`}>
      <span>{texts.offerEndsIn}</span>
      {left && left.days > 0 && <span>{texts.offerDays.replace("{n}", new Intl.NumberFormat(locale).format(left.days))}</span>}
      <time dateTime={endsUtc} dir="ltr" className="tabular-nums">
        {left ? `${two.format(left.hours)}:${two.format(left.minutes)}:${two.format(left.seconds)}` : ""}
      </time>
    </span>
  );
}
