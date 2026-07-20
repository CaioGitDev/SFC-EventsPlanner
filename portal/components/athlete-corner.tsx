import Link from "next/link";
import { AthleteAvatar } from "@/components/athlete-avatar";
import { RecordBadge } from "@/components/record-badge";
import { flagFor } from "@/lib/flags";
import type { CardAthlete, Corner } from "@/lib/types";
import { cn } from "@/lib/utils";

export function AthleteCorner({
  athlete,
  corner,
  align,
}: {
  athlete: CardAthlete;
  corner: Corner;
  align: "left" | "right";
}) {
  const flag = flagFor(athlete.nationality);
  const ring = corner === "red" ? "ring-primary" : "ring-accent";

  const inner = (
    <div
      className={cn(
        "flex flex-1 flex-col items-center gap-2 text-center",
        align === "left" ? "sm:items-start sm:text-left" : "sm:items-end sm:text-right",
      )}
    >
      <AthleteAvatar
        src={athlete.photoUrl}
        name={athlete.name}
        className={cn("size-20 text-lg ring-2 sm:size-24", ring)}
      />
      <div>
        <p className="font-heading text-base font-bold leading-tight sm:text-lg">
          {flag && <span className="mr-1">{flag}</span>}
          {athlete.name}
        </p>
        {athlete.nickname && (
          <p className="text-sm text-muted-foreground">“{athlete.nickname}”</p>
        )}
        {athlete.record && <RecordBadge record={athlete.record} className="mt-1" />}
      </div>
    </div>
  );

  // Only consented athletes carry a slug (the API redacts the rest) → only they link out.
  return athlete.slug ? (
    <Link
      href={`/fighters/${athlete.slug}`}
      className="flex flex-1 rounded-lg transition-opacity hover:opacity-80"
    >
      {inner}
    </Link>
  ) : (
    inner
  );
}
