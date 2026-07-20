import { AthleteCorner } from "@/components/athlete-corner";
import { Badge } from "@/components/badge";
import { billingLabel, disciplineLabel, weightLabel } from "@/lib/labels";
import type { FightCardEntry } from "@/lib/types";

export function FightCard({ fight }: { fight: FightCardEntry }) {
  return (
    <div className="rounded-xl border border-border/60 bg-card/50 p-4 sm:p-5">
      <div className="mb-4 flex flex-wrap items-center justify-center gap-2 text-xs">
        <Badge
          variant={
            fight.billing === "Main"
              ? "danger"
              : fight.billing === "CoMain"
                ? "accent"
                : "muted"
          }
        >
          {billingLabel(fight.billing)}
        </Badge>
        {fight.isTitleFight && <Badge variant="title">Título</Badge>}
        {fight.isAmateur && <Badge variant="muted">Amador</Badge>}
        {fight.status === "Cancelled" && <Badge variant="danger">Cancelado</Badge>}
        {fight.status === "NoContest" && <Badge variant="muted">No contest</Badge>}
        <span className="text-muted-foreground">
          {disciplineLabel(fight.discipline)} ·{" "}
          {weightLabel(fight.weightClass, fight.catchweightKg)} · {fight.rounds}×
          {fight.roundDurationMinutes}min
        </span>
      </div>
      <div className="flex items-center gap-3 sm:gap-5">
        <AthleteCorner athlete={fight.red} corner="red" align="left" />
        <span className="font-heading text-xl font-black text-muted-foreground sm:text-2xl">
          VS
        </span>
        <AthleteCorner athlete={fight.blue} corner="blue" align="right" />
      </div>
    </div>
  );
}
