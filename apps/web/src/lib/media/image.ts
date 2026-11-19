/** Stored image sizes (longest side), as the API's MediaUrls.Sizes. */
export const imageSizes = [1200, 600, 300] as const;
export type ImageSize = (typeof imageSizes)[number];

const mediaUrl = /^\/api\/v1\/media\/[0-9a-f]{32}\/(1200|600|300)\.webp$/;

/** Whether an address is an uploaded image (served by the API), as opposed to a demo SVG or another address. */
export function isMediaUrl(url: string | null | undefined): url is string {
  return typeof url === "string" && mediaUrl.test(url);
}

/** The address of an uploaded image in another size; any other address comes back unchanged. */
export function imageVariant(url: string, size: ImageSize): string {
  return isMediaUrl(url) ? url.replace(/\/(1200|600|300)\.webp$/, `/${size}.webp`) : url;
}
