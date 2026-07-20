import Image from "next/image";
import Link from "next/link";
import { Countdown } from "@/components/countdown";
import { formatDateTime, isTodayInLisbon } from "@/lib/format";
import type { EventSummary } from "@/lib/types";

export function EventHero({ event }: { event: EventSummary }) {
  const live = Boolean(event.streamUrl) && isTodayInLisbon(event.date);
  const location = [event.venue, event.city].filter(Boolean).join(", ");

  return (
    <section className="relative overflow-hidden border-b border-border/60">
      {event.bannerUrl && (
        <Image
          src={event.bannerUrl}
          alt=""
          fill
          priority
          className="object-cover opacity-30"
        />
      )}
      <div className="absolute inset-0 bg-gradient-to-t from-background via-background/85 to-background/40" />

      <div className="relative mx-auto max-w-6xl px-4 py-16 sm:py-24">
        <p className="font-heading text-sm font-bold uppercase tracking-widest text-primary">
          Próximo evento
        </p>
        <h1 className="mt-2 max-w-3xl font-heading text-4xl font-black leading-none tracking-tight sm:text-6xl">
          {event.name}
        </h1>
        <p className="mt-3 text-lg text-muted-foreground">
          {formatDateTime(event.date)}
          {location && <> · {location}</>}
        </p>

        <div className="mt-8">
          <Countdown targetIso={event.date} />
        </div>

        <div className="mt-8 flex flex-wrap gap-3">
          <Link
            href={`/events/${event.slug}`}
            className="rounded-lg bg-primary px-5 py-3 font-heading font-bold text-primary-foreground transition-opacity hover:opacity-90"
          >
            Ver fight card
          </Link>
          {live && (
            <a
              href={event.streamUrl ?? "#"}
              target="_blank"
              rel="noopener noreferrer"
              className="rounded-lg bg-accent px-5 py-3 font-heading font-bold text-accent-foreground transition-opacity hover:opacity-90"
            >
              Ver em direto
            </a>
          )}
          {event.ticketsUrl && (
            <a
              href={event.ticketsUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="rounded-lg border border-border px-5 py-3 font-heading font-bold transition-colors hover:bg-secondary"
            >
              Bilhetes
            </a>
          )}
        </div>
      </div>
    </section>
  );
}
