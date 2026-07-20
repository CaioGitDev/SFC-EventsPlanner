import Link from "next/link";
import { Badge } from "@/components/badge";
import { flagFor } from "@/lib/flags";
import { billingLabel, disciplineLabel, methodLabel, weightLabel } from "@/lib/labels";
import type { CardAthlete, FightResultRow } from "@/lib/types";
import { cn } from "@/lib/utils";

function AthleteName({
  athlete,
  winner,
  align,
}: {
  athlete: CardAthlete;
  winner: boolean;
  align: "left" | "right";
}) {
  const flag = flagFor(athlete.nationality);
  const content = (
    <span
      className={cn(
        "font-heading font-bold",
        winner ? "text-foreground" : "text-muted-foreground",
      )}
    >
      {flag && <span className="mr-1">{flag}</span>}
      {athlete.name}
    </span>
  );
  return (
    <div className={cn("flex-1", align === "right" ? "text-right" : "text-left")}>
      {athlete.slug ? (
        <Link href={`/fighters/${athlete.slug}`} className="hover:underline">
          {content}
        </Link>
      ) : (
        content
      )}
    </div>
  );
}

function verdict(row: FightResultRow): string {
  if (row.status === "Cancelled") return "Cancelado";
  if (row.status === "NoContest" || row.result?.method === "NoContest") return "No contest";
  if (!row.result) return "Por decidir";
  if (row.result.method === "Draw") return "Empate";
  const parts = [methodLabel(row.result.method)];
  if (row.result.round != null) {
    parts.push(`R${row.result.round}${row.result.time ? ` ${row.result.time}` : ""}`);
  }
  return parts.join(" · ");
}

export function ResultLine({ row }: { row: FightResultRow }) {
  return (
    <div className="rounded-xl border border-border/60 bg-card/50 p-4">
      <div className="mb-3 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <Badge
          variant={
            row.billing === "Main"
              ? "danger"
              : row.billing === "CoMain"
                ? "accent"
                : "muted"
          }
        >
          {billingLabel(row.billing)}
        </Badge>
        {row.isTitleFight && <Badge variant="title">Título</Badge>}
        {row.isAmateur && <Badge variant="muted">Amador</Badge>}
        <span>
          {disciplineLabel(row.discipline)} ·{" "}
          {weightLabel(row.weightClass, row.catchweightKg)}
        </span>
      </div>
      <div className="flex items-center gap-3">
        <AthleteName
          athlete={row.red}
          winner={row.result?.winnerCorner === "red"}
          align="left"
        />
        <span className="shrink-0 text-center font-mono text-xs uppercase tracking-wide text-primary">
          {verdict(row)}
        </span>
        <AthleteName
          athlete={row.blue}
          winner={row.result?.winnerCorner === "blue"}
          align="right"
        />
      </div>
    </div>
  );
}
