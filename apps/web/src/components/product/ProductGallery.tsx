"use client";

import Image from "next/image";
import { useState } from "react";
import { Icon } from "@/components/ui/Icon";
import { imageVariant, isMediaUrl } from "@/lib/media/image";

export type GalleryImage = { url: string; alt: string | null };
export type GalleryVideo = { url: string; posterUrl: string | null; title: string | null };

type Props = { images: GalleryImage[]; videos?: GalleryVideo[]; name: string; label: string; videoLabel?: string };

type Item = { kind: "image"; image: GalleryImage } | { kind: "video"; video: GalleryVideo; poster: string | null };

/** A video's still image: its own poster, else the product's main image (large or small). */
export function posterFor(video: GalleryVideo, images: GalleryImage[], size: 1200 | 300): string | null {
  const url = video.posterUrl ?? images[0]?.url ?? null;
  return url && isMediaUrl(url) ? imageVariant(url, size) : url;
}

/**
 * The product's images, then its videos: the current item large (fixed square, no layout shift) and, when there are
 * more, thumbnails that switch it. Every image has its alternative text, else the product name. Videos only load when
 * played (preload="none") and never start by themselves. The server renders the first item, so the page works
 * without JavaScript.
 */
export function ProductGallery({ images, videos = [], name, label, videoLabel = "Video" }: Props) {
  const items: Item[] = [
    ...images.map((image) => ({ kind: "image" as const, image })),
    ...videos.map((video) => ({ kind: "video" as const, video, poster: posterFor(video, images, 1200) })),
  ];
  const [current, setCurrent] = useState(0);
  const main = items[current] ?? items[0];
  if (!main) return <div className="aspect-square rounded-[calc(var(--radius)*1.75)] border border-border bg-surface" />;

  return (
    <div className="flex flex-col gap-2">
      <div className="overflow-hidden rounded-[calc(var(--radius)*1.75)] border border-border bg-surface">
        {main.kind === "image" ? (
          <Image
            src={imageVariant(main.image.url, 1200)}
            alt={main.image.alt || name}
            width={600}
            height={600}
            unoptimized
            priority={current === 0}
            className="aspect-square h-auto w-full object-contain"
          />
        ) : (
          <video
            key={main.video.url}
            src={main.video.url}
            poster={main.poster ?? undefined}
            controls
            preload="none"
            playsInline
            aria-label={main.video.title || `${name} – ${videoLabel}`}
            className="aspect-square h-auto w-full bg-black object-contain"
          />
        )}
      </div>
      {items.length > 1 && (
        <ul aria-label={label} className="flex gap-2 overflow-x-auto">
          {items.map((item, i) => {
            const thumb = item.kind === "image" ? imageVariant(item.image.url, 300) : posterFor(item.video, images, 300);
            const itemLabel = item.kind === "image" ? item.image.alt || `${name} ${i + 1}` : item.video.title || `${name} – ${videoLabel}`;
            return (
              <li key={item.kind === "image" ? item.image.url : item.video.url} className="shrink-0">
                <button
                  type="button"
                  onClick={() => setCurrent(i)}
                  aria-current={i === current ? "true" : undefined}
                  aria-label={itemLabel}
                  className="relative block h-16 w-16 overflow-hidden rounded-lg border-2 border-transparent bg-black/5 aria-[current]:border-primary"
                >
                  {thumb && <Image src={thumb} alt="" width={64} height={64} unoptimized className="h-full w-full object-cover" />}
                  {item.kind === "video" && (
                    <span aria-hidden className="absolute inset-0 grid place-items-center bg-black/30 text-white">
                      <Icon name="play" className="h-7 w-7" />
                    </span>
                  )}
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
