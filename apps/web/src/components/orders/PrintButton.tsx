"use client";

import { Button } from "@/components/ui/Button";

/** Opens the browser's print dialog, where "Save as PDF" makes the invoice file. Not printed itself. */
export function PrintButton({ label }: { label: string }) {
  return (
    <Button onClick={() => window.print()} className="print:hidden">
      {label}
    </Button>
  );
}
