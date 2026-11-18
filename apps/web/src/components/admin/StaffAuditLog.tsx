import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatList } from "@/lib/i18n/format-list";
import { roleName } from "./RoleCheckboxes";

type Entry = components["schemas"]["StaffAuditView"];

/** The latest staff changes: when, who, what, to whom, and the resulting roles where they changed. */
export function StaffAuditLog({ locale, texts, entries }: { locale: string; texts: Dictionary["admin"]; entries: readonly Entry[] }) {
  if (entries.length === 0) return <p className="text-sm text-muted">{texts.noAudit}</p>;

  return (
    <ol className="flex flex-col gap-1 text-sm">
      {entries.map((entry) => {
        // Accepting is done by the target themselves: "X accepted the invitation" needs no second name.
        const target = entry.action === "InvitationAccepted" ? null : (entry.targetName ?? entry.targetIdentifier);
        const roles = entry.details ? entry.details.split(",").filter(Boolean) : [];
        return (
          <li key={entry.id} className="flex flex-wrap gap-x-2 border-b border-border py-1 last:border-0">
            <time dateTime={entry.createdUtc} className="text-xs text-muted">
              {formatDate(entry.createdUtc, locale, true)}
            </time>
            <span className="font-medium">{entry.actorName ?? entry.actorUserId}</span>
            <span>{(texts.auditActions as Record<string, string>)[entry.action] ?? entry.action}</span>
            {target && (
              <bdi dir="auto" className="font-medium">
                {target}
              </bdi>
            )}
            {roles.length > 0 && <span className="text-muted">({formatList(roles.map((r) => roleName(texts, r)), locale)})</span>}
          </li>
        );
      })}
    </ol>
  );
}
