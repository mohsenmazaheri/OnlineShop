"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { FormAlert, SubmitButton } from "@/components/account/FormParts";
import { useApiForm } from "@/components/account/useApiForm";
import { Button } from "@/components/ui/Button";
import type { ErrorTexts } from "@/lib/account/problems";
import { refreshStorefront } from "@/lib/admin/refresh";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { LocalizedFields, readLocalizedFields, type LocalizedText } from "./LocalizedFields";

type CatalogTree = components["schemas"]["CatalogTree"];
type Texts = Dictionary["admin"];
export type NodeKind = "category" | "subcategory" | "group";

/** Where a node is created, changed and deleted. */
export function nodeEndpoints(kind: NodeKind, id?: string, parentId?: string): { create: `/api/v1/${string}`; item: `/api/v1/${string}` } {
  const base = "/api/v1/admin/catalog";
  const collection = { category: "categories", subcategory: "subcategories", group: "groups" }[kind];
  const create =
    kind === "category" ? `${base}/categories` : kind === "subcategory" ? `${base}/categories/${parentId}/subcategories` : `${base}/subcategories/${parentId}/groups`;
  return { create: create as `/api/v1/${string}`, item: `${base}/${collection}/${id}` };
}

/** The name shown for a node: the admin's language when translated, otherwise the default language. */
export function displayName(texts: LocalizedText[], locale: string): string {
  return texts.find((t) => t.locale === locale && t.name)?.name ?? texts[0]?.name ?? "";
}

type Shared = { locale: string; texts: Texts; tree: CatalogTree; languageNames: Record<string, string> };

function NodeForm({ kind, id, parentId, values, onDone, ...shared }: Shared & { kind: NodeKind; id?: string; parentId?: string; values: LocalizedText[]; onDone?: () => void }) {
  const { locale, texts, tree, languageNames } = shared;
  const router = useRouter();
  const { busy, submit, formError } = useApiForm({ ...texts.errors, ...texts.catalogErrors } as ErrorTexts);
  const [saved, setSaved] = useState(false);
  const endpoints = nodeEndpoints(kind, id, parentId);

  async function done() {
    await refreshStorefront(locale, ["catalog"]);
    router.refresh();
    onDone?.();
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const element = event.currentTarget;
    const body = { texts: readLocalizedFields(new FormData(element), tree.locales, `${kind}-${id ?? "new"}-`) };
    const result = await submit(id ? endpoints.item : endpoints.create, body, id ? "PUT" : "POST");
    if (result.ok) {
      setSaved(true);
      if (!id) element.reset();
      await done();
    }
  }

  async function remove() {
    if (!id || !window.confirm(texts.confirmDeleteNode)) return;
    const result = await submit(endpoints.item, undefined, "DELETE");
    if (result.ok) await done();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-3">
      {formError && <FormAlert kind="error">{formError}</FormAlert>}
      {saved && id && <FormAlert kind="success">{texts.saved}</FormAlert>}
      <LocalizedFields
        locales={tree.locales}
        defaultLocale={tree.defaultLocale}
        values={values}
        languageNames={languageNames}
        texts={texts}
        withSlug={kind !== "group"}
        prefix={`${kind}-${id ?? "new"}-`}
      />
      <div className="flex flex-wrap gap-2">
        <SubmitButton busy={busy} label={id ? texts.save : texts.add} busyLabel="…" />
        {id && (
          <Button variant="danger" onClick={remove} disabled={busy}>
            {texts.remove}
          </Button>
        )}
      </div>
    </form>
  );
}

function Node({ title, meta, children }: { title: string; meta?: string; children: React.ReactNode }) {
  return (
    <details className="rounded-xl border border-border bg-surface p-3 open:shadow-sm">
      <summary className="flex cursor-pointer items-center justify-between gap-2 font-medium">
        <span>{title}</span>
        {meta && <span className="text-xs text-muted">{meta}</span>}
      </summary>
      <div className="mt-3 flex flex-col gap-3">{children}</div>
    </details>
  );
}

/**
 * The whole category tree: every node opens to its form (names and slugs per language, save, delete) and to the forms
 * that add children. Deleting works only for empty nodes; the API explains otherwise.
 */
export function CatalogTreeEditor(shared: Shared) {
  const { locale, texts, tree } = shared;
  const empty: LocalizedText[] = [];

  return (
    <div className="flex flex-col gap-3">
      {tree.categories.length === 0 && <p className="text-sm text-muted">{texts.catalogEmpty}</p>}
      {tree.categories.map((category) => (
        <Node key={category.id} title={displayName(category.texts, locale)} meta={texts.childCount.replace("{count}", String(category.subCategories.length))}>
          <NodeForm {...shared} kind="category" id={category.id} values={category.texts} />
          <div className="flex flex-col gap-2 border-s-2 border-border ps-3">
            {category.subCategories.map((sub) => (
              <Node key={sub.id} title={displayName(sub.texts, locale)} meta={texts.childCount.replace("{count}", String(sub.groups.length))}>
                <NodeForm {...shared} kind="subcategory" id={sub.id} values={sub.texts} />
                <div className="flex flex-col gap-2 border-s-2 border-border ps-3">
                  {sub.groups.map((group) => (
                    <Node key={group.id} title={displayName(group.texts, locale)} meta={texts.productCount.replace("{count}", String(group.productCount))}>
                      <NodeForm {...shared} kind="group" id={group.id} values={group.texts} />
                    </Node>
                  ))}
                  <Node title={`+ ${texts.addGroup}`}>
                    <NodeForm {...shared} kind="group" parentId={sub.id} values={empty} />
                  </Node>
                </div>
              </Node>
            ))}
            <Node title={`+ ${texts.addSubCategory}`}>
              <NodeForm {...shared} kind="subcategory" parentId={category.id} values={empty} />
            </Node>
          </div>
        </Node>
      ))}
      <Node title={`+ ${texts.addCategory}`}>
        <NodeForm {...shared} kind="category" values={empty} />
      </Node>
    </div>
  );
}
