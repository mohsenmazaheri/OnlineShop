"use client";

import Link from "next/link";
import { Icon } from "@/components/ui/Icon";
import { compareStore } from "@/lib/compare/store";

/**
 * Takes a product out of the comparison: a link to the same page without it (works without JavaScript), which also
 * takes it off the shopper's list.
 */
export function CompareRemove({ href, number, label }: { href: string; number: number; label: string }) {
  return (
    <Link
      href={href}
      rel="nofollow"
      aria-label={label}
      title={label}
      onClick={() => compareStore().remove(number)}
      className="grid h-9 w-9 shrink-0 place-items-center rounded-full border border-border text-muted hover:text-danger"
    >
      <Icon name="close" size={16} />
    </Link>
  );
}
