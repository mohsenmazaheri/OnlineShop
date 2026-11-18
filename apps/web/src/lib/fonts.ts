import localFont from "next/font/local";

/*
 * Self-hosted fonts (OFL), bundled at build time from npm packages: no font request ever leaves the shop's server
 * (also required for Iran and for GDPR). Nothing is preloaded: one layout serves every language, and the browser
 * downloads only the font the page's language uses (globals.css picks it by :lang). Fallback metrics keep the text
 * from shifting when the font arrives.
 */
/**
 * Arabic-script letters only (Persian and Arabic, Persian digits, ZWNJ): Vazirmatn comes first for every language and
 * the browser uses — and downloads — it only for those characters, so Persian content that falls back to the default
 * language also looks right on English or Armenian pages. Latin text goes on to Inter. No automatic Arial fallback
 * here: it would sit in front of Inter and catch the Latin text.
 */
export const arabicScriptRange = "U+0600-06FF, U+0750-077F, U+08A0-08FF, U+FB50-FDFF, U+FE70-FEFF, U+200C-200F";

export const vazirmatn = localFont({
  src: "../../node_modules/vazirmatn/fonts/webfonts/Vazirmatn[wght].woff2",
  weight: "100 900",
  variable: "--font-vazirmatn",
  display: "swap",
  preload: false,
  adjustFontFallback: false,
  // Must be a literal (next/font requirement); keep it equal to arabicScriptRange.
  declarations: [{ prop: "unicode-range", value: "U+0600-06FF, U+0750-077F, U+08A0-08FF, U+FB50-FDFF, U+FE70-FEFF, U+200C-200F" }],
});

export const inter = localFont({
  src: "../../node_modules/@fontsource-variable/inter/files/inter-latin-wght-normal.woff2",
  weight: "100 900",
  variable: "--font-inter",
  display: "swap",
  preload: false,
  adjustFontFallback: "Arial",
});

export const armenian = localFont({
  src: "../../node_modules/@fontsource-variable/noto-sans-armenian/files/noto-sans-armenian-armenian-wght-normal.woff2",
  weight: "100 900",
  variable: "--font-armenian",
  display: "swap",
  preload: false,
  adjustFontFallback: "Arial",
});

/** Class names that define the three font variables on <html>. */
export const fontVariables = `${vazirmatn.variable} ${inter.variable} ${armenian.variable}`;
