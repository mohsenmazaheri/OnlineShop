import Link from "next/link";
import type { ButtonHTMLAttributes, ComponentProps } from "react";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";
export type ButtonSize = "sm" | "md" | "lg";

const variants: Record<ButtonVariant, string> = {
  primary: "bg-primary text-primary-foreground hover:opacity-90",
  secondary: "border border-border bg-surface text-foreground hover:bg-surface-muted",
  ghost: "text-primary hover:bg-surface-muted",
  danger: "bg-danger text-danger-foreground hover:opacity-90",
};

// Touch targets: md and lg meet the 44px minimum; sm is for dense desktop toolbars.
const sizes: Record<ButtonSize, string> = {
  sm: "h-9 px-3 text-sm",
  md: "h-11 px-4 text-sm",
  lg: "h-12 px-5 text-base",
};

/** Classes for anything that should look like a button (buttons, links, submit buttons). */
export function buttonClass(variant: ButtonVariant = "primary", size: ButtonSize = "md", extra = ""): string {
  return [
    "inline-flex items-center justify-center gap-2 rounded-xl font-semibold transition-[opacity,background-color] disabled:pointer-events-none disabled:opacity-60",
    variants[variant],
    sizes[size],
    extra,
  ]
    .filter(Boolean)
    .join(" ");
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant; size?: ButtonSize };

/** A button; type="button" unless said otherwise, so it never submits a form by accident. */
export function Button({ variant, size, className, type = "button", ...props }: ButtonProps) {
  return <button type={type} className={buttonClass(variant, size, className)} {...props} />;
}

type ButtonLinkProps = ComponentProps<typeof Link> & { variant?: ButtonVariant; size?: ButtonSize };

/** A link styled as a button (navigation stays a real link: works without JavaScript, can be opened in a new tab). */
export function ButtonLink({ variant, size, className, ...props }: ButtonLinkProps) {
  return <Link className={buttonClass(variant, size, className)} {...props} />;
}
