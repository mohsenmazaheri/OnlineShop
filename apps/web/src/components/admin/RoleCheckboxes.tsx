import type { Dictionary } from "@/lib/i18n/dictionaries";

/** Staff roles in the order the API uses (fixed in code; see AppRoles.Staff). */
export const staffRoles = ["Admin", "Manager", "Warehouse", "Accountant"] as const;
export type StaffRole = (typeof staffRoles)[number];

export function roleName(texts: Dictionary["admin"], role: string): string {
  return (texts.roles as Record<string, string>)[role] ?? role;
}

type Props = {
  texts: Dictionary["admin"];
  name: string;
  /** Roles ticked at first render (uncontrolled: the form reads the checkboxes on submit). */
  defaultChecked?: readonly string[];
  error?: string;
};

/** One checkbox per staff role, each with a short description of what it can do. */
export function RoleCheckboxes({ texts, name, defaultChecked = [], error }: Props) {
  return (
    <fieldset className="flex flex-col gap-2" aria-invalid={error ? true : undefined}>
      <legend className="mb-1 text-sm font-medium">{texts.rolesLabel}</legend>
      {staffRoles.map((role) => (
        <label key={role} className="flex items-start gap-2 text-sm">
          <input type="checkbox" name={name} value={role} defaultChecked={defaultChecked.includes(role)} className="mt-1 h-4 w-4 accent-[var(--primary)]" />
          <span>
            <span className="font-medium">{roleName(texts, role)}</span>
            <span className="block text-xs text-muted">{(texts.roleHelp as Record<string, string>)[role]}</span>
          </span>
        </label>
      ))}
      {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}
    </fieldset>
  );
}
