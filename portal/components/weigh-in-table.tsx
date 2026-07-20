import Link from "next/link";
import { Badge } from "@/components/badge";
import { weightLabel } from "@/lib/labels";
import type { WeighInRow } from "@/lib/types";
import { cn } from "@/lib/utils";

export function WeighInTable({ rows }: { rows: WeighInRow[] }) {
  return (
    <div className="overflow-hidden rounded-xl border border-border/60">
      <table className="w-full text-sm">
        <thead className="bg-secondary/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
          <tr>
            <th className="px-4 py-3 font-semibold">Atleta</th>
            <th className="px-4 py-3 font-semibold">Categoria</th>
            <th className="px-4 py-3 text-right font-semibold">Peso oficial</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr
              key={`${row.order}-${row.corner}-${i}`}
              className="border-t border-border/40"
            >
              <td className="px-4 py-3">
                <span
                  className={cn(
                    "mr-2 inline-block size-2 rounded-full align-middle",
                    row.corner === "red" ? "bg-primary" : "bg-accent",
                  )}
                  aria-label={row.corner === "red" ? "Canto vermelho" : "Canto azul"}
                />
                {row.athleteSlug ? (
                  <Link
                    href={`/fighters/${row.athleteSlug}`}
                    className="font-medium hover:underline"
                  >
                    {row.athleteName}
                  </Link>
                ) : (
                  <span className="font-medium">{row.athleteName}</span>
                )}
              </td>
              <td className="px-4 py-3 text-muted-foreground">
                {weightLabel(row.weightClass, row.catchweightKg)}
              </td>
              <td className="px-4 py-3 text-right">
                <span className="font-mono">
                  {row.officialWeightKg != null ? `${row.officialWeightKg} kg` : "—"}
                </span>
                {row.missedWeight && (
                  <Badge variant="danger" className="ml-2">
                    Não fez peso
                  </Badge>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
