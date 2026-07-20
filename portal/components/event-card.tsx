import Image from "next/image";
import Link from "next/link";
import { Badge } from "@/components/badge";
import { formatDate } from "@/lib/format";
import { eventStatusLabel } from "@/lib/labels";
import type { EventSummary } from "@/lib/types";

export function EventCard({ event }: { event: EventSummary }) {
  const location = [event.venue, event.city].filter(Boolean).join(", ");

  return (
    <Link
      href={`/events/${event.slug}`}
      className="group flex flex-col overflow-hidden rounded-xl border border-border/60 bg-card/50 transition-colors hover:border-primary/60"
    >
      <div className="relative aspect-[16/9] bg-secondary">
        {event.bannerUrl ? (
          <Image
            src={event.bannerUrl}
            alt=""
            fill
            className="object-cover transition-transform duration-300 group-hover:scale-105"
          />
        ) : (
          <div className="grid h-full place-items-center font-heading text-4xl font-black text-muted-foreground/40">
            SFC
          </div>
        )}
        {event.status === "Cancelled" && (
          <div className="absolute right-2 top-2">
            <Badge variant="danger">{eventStatusLabel(event.status)}</Badge>
          </div>
        )}
      </div>
      <div className="flex flex-1 flex-col gap-1 p-4">
        <h3 className="font-heading text-lg font-bold leading-tight">{event.name}</h3>
        <p className="text-sm text-muted-foreground">{formatDate(event.date)}</p>
        {location && <p className="text-sm text-muted-foreground">{location}</p>}
        <p className="mt-auto pt-2 text-xs uppercase tracking-wide text-muted-foreground">
          {event.fightCount} {event.fightCount === 1 ? "combate" : "combates"}
        </p>
      </div>
    </Link>
  );
}
