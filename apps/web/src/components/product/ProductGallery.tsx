"use client";

import Image from "next/image";
import { useState } from "react";
import { imageVariant } from "@/lib/media/image";

export type GalleryImage = { url: string; alt: string | null };

type Props = { images: GalleryImage[]; name: string; label: string };

/**
 * The product's images: the main one large (loaded first, fixed square, no layout shift) and, when there are more,
 * thumbnails that switch it. Every image has its alternative text, else the product name. The server renders the
 * first image, so the page works without JavaScript.
 */
export function ProductGallery({ images, name, label }: Props) {
  const [current, setCurrent] = useState(0);
  const main = images[current] ?? images[0];
  if (!main) return <div className="aspect-square rounded-[calc(var(--radius)*1.75)] border border-border bg-surface" />;

  return (
    <div className="flex flex-col gap-2">
      <div className="overflow-hidden rounded-[calc(var(--radius)*1.75)] border border-border bg-surface">
        <Image
          src={imageVariant(main.url, 1200)}
          alt={main.alt || name}
          width={600}
          height={600}
          unoptimized
          priority={current === 0}
          className="aspect-square h-auto w-full object-contain"
        />
      </div>
      {images.length > 1 && (
        <ul aria-label={label} className="flex gap-2 overflow-x-auto">
          {images.map((image, i) => (
            <li key={image.url} className="shrink-0">
              <button
                type="button"
                onClick={() => setCurrent(i)}
                aria-current={i === current ? "true" : undefined}
                aria-label={image.alt || `${name} ${i + 1}`}
                className="block h-16 w-16 overflow-hidden rounded-lg border-2 border-transparent aria-[current]:border-primary"
              >
                <Image src={imageVariant(image.url, 300)} alt="" width={64} height={64} unoptimized className="h-full w-full object-cover" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
