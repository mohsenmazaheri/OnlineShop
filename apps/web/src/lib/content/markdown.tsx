import type { ReactNode } from "react";

/**
 * The marks a post or page may use (no HTML ever reaches the page, so a text can never inject a script):
 * blank-line separated paragraphs, "## " and "### " headings, "- " / "* " and "1. " lists, "> " quotes, and inline
 * **bold** and [links](address) to the shop's own pages (/…), #anchors, http(s) or mailto.
 */
export type Block =
  | { kind: "h2" | "h3" | "p" | "quote"; text: string }
  | { kind: "ul" | "ol"; items: string[] };

export function parseBlocks(source: string): Block[] {
  return source
    .replace(/\r\n?/g, "\n")
    .split(/\n\s*\n/)
    .map((block) => block.trim())
    .filter((block) => block.length > 0)
    .map((block): Block => {
      const lines = block.split("\n").map((l) => l.trim());
      if (lines.length === 1 && block.startsWith("### ")) return { kind: "h3", text: block.slice(4).trim() };
      if (lines.length === 1 && block.startsWith("## ")) return { kind: "h2", text: block.slice(3).trim() };
      if (lines.every((l) => /^[-*]\s+/.test(l))) return { kind: "ul", items: lines.map((l) => l.replace(/^[-*]\s+/, "")) };
      if (lines.every((l) => /^\d+[.)]\s+/.test(l))) return { kind: "ol", items: lines.map((l) => l.replace(/^\d+[.)]\s+/, "")) };
      if (lines.every((l) => l.startsWith(">"))) return { kind: "quote", text: lines.map((l) => l.replace(/^>\s?/, "")).join("\n") };
      return { kind: "p", text: lines.join("\n") };
    });
}

/** Whether a link may point there (no javascript: or data: addresses). */
export function safeHref(href: string): boolean {
  return /^(\/(?!\/)|#|https?:\/\/|mailto:)/i.test(href);
}

const inline = /(\*\*[^*]+\*\*|\[[^\]]+\]\([^)\s]+\))/g;

/** Bold, links and line breaks inside a block, as React nodes. */
export function renderInline(text: string, keyPrefix = "i"): ReactNode[] {
  const nodes: ReactNode[] = [];
  text.split(inline).forEach((part, i) => {
    const key = `${keyPrefix}-${i}`;
    const link = /^\[([^\]]+)\]\(([^)\s]+)\)$/.exec(part);
    if (part.startsWith("**") && part.endsWith("**") && part.length > 4) {
      nodes.push(<strong key={key}>{part.slice(2, -2)}</strong>);
    } else if (link && safeHref(link[2])) {
      const external = /^https?:\/\//i.test(link[2]);
      nodes.push(
        <a key={key} href={link[2]} rel={external ? "noopener" : undefined} className="text-primary underline underline-offset-2">
          {link[1]}
        </a>,
      );
    } else if (link) {
      nodes.push(link[1]);
    } else {
      part.split("\n").forEach((line, j, lines) => {
        nodes.push(line);
        if (j < lines.length - 1) nodes.push(<br key={`${key}-br-${j}`} />);
      });
    }
  });
  return nodes;
}

/** The text of a post or page as HTML elements (headings start at h2: the page title is the h1). */
export function ContentBody({ source }: { source: string }) {
  return (
    <div className="flex max-w-3xl flex-col gap-4 leading-loose">
      {parseBlocks(source).map((block, i) => {
        switch (block.kind) {
          case "h2":
            return (
              <h2 key={i} className="mt-2 text-xl font-extrabold">
                {renderInline(block.text, `b${i}`)}
              </h2>
            );
          case "h3":
            return (
              <h3 key={i} className="text-lg font-bold">
                {renderInline(block.text, `b${i}`)}
              </h3>
            );
          case "quote":
            return (
              <blockquote key={i} className="border-s-4 border-primary ps-4 text-muted">
                {renderInline(block.text, `b${i}`)}
              </blockquote>
            );
          case "ul":
          case "ol": {
            const List = block.kind;
            return (
              <List key={i} className={`flex flex-col gap-1 ps-6 ${block.kind === "ul" ? "list-disc" : "list-decimal"}`}>
                {block.items.map((item, j) => (
                  <li key={j}>{renderInline(item, `b${i}-${j}`)}</li>
                ))}
              </List>
            );
          }
          default:
            return <p key={i}>{renderInline(block.text, `b${i}`)}</p>;
        }
      })}
    </div>
  );
}
