/** The page links to show for a long list: the first, the last and two either side of the current one, in order. */
export function pageWindow(current: number, pages: number): number[] {
  const wanted = new Set([1, pages, current - 2, current - 1, current, current + 1, current + 2]);
  return [...wanted].filter((p) => p >= 1 && p <= pages).sort((a, b) => a - b);
}
