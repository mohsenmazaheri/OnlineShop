import { notFound } from "next/navigation";
import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ProductEditor } from "@/components/admin/ProductEditor";
import { ProductImagesEditor } from "@/components/admin/ProductImagesEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getAdminProduct, getCatalogTree, getSpecChoices } from "@/lib/account/session";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/products/[number]">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.productEdit);
}

type Params = PageProps<"/[locale]/admin/products/[number]">["params"];

/** Edit one product (permission catalog.manage); the number comes from the URL, so it is read inside Suspense. */
async function EditProduct({ params }: { params: Params }) {
  const { locale: raw, number: rawNumber } = await params;
  const locale = routeLocale(raw);
  const texts: Dictionary["admin"] = (await getDictionary(locale)).admin;
  const user = await requirePermission(locale, "catalog.manage", `/${locale}/admin/products/${rawNumber}`);
  const number = /^\d{1,15}$/.test(rawNumber) ? Number(rawNumber) : null;
  if (number === null) notFound();
  const [product, tree, specs] = await Promise.all([getAdminProduct(number), getCatalogTree(), getSpecChoices(locale)]);
  if (!product) notFound();
  const catalog = tree ?? { defaultLocale: locale, locales: [locale], categories: [] };

  return (
    <AdminShell locale={locale} texts={texts} current="products" permissions={user.permissions}>
      <AdminCard title={`${texts.productEdit} #${product.number}`} id="product-edit">
        <ProductEditor
          locale={locale}
          texts={texts}
          tree={catalog}
          specs={specs ?? []}
          languageNames={languageNames(catalog.locales)}
          product={product}
        />
      </AdminCard>
      <AdminCard title={texts.imagesTitle} id="product-images">
        <ProductImagesEditor locale={locale} number={product.number} images={product.images} languageNames={languageNames(catalog.locales)} texts={texts} />
      </AdminCard>
    </AdminShell>
  );
}

/** The product number is only known per request: everything, the texts included, loads inside Suspense. */
export default function EditProductPage({ params }: PageProps<"/[locale]/admin/products/[number]">) {
  return (
    <Suspense fallback={<p role="status" className="p-6">…</p>}>
      <EditProduct params={params} />
    </Suspense>
  );
}
