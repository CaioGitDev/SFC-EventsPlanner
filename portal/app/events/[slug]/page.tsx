import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Badge } from "@/components/badge";
import { FightCard } from "@/components/fight-card";
import { Poster } from "@/components/poster";
import { getEvent, getEvents } from "@/lib/api";
import { formatDateTime, isTodayInLisbon } from "@/lib/format";
import { eventStatusLabel } from "@/lib/labels";

export const revalidate = 3600;
export const dynamicParams = true;

export async function generateStaticParams() {
  const list = await getEvents();
  const all = [...(list?.upcoming ?? []), ...(list?.past ?? [])];
  return all.map((e) => ({ slug: e.slug }));
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string }>;
}): Promise<Metadata> {
  const { slug } = await params;
  const event = await getEvent(slug);
  if (!event) return { title: "Evento não encontrado" };

  const image = event.posterUrl ?? event.bannerUrl;
  return {
    title: event.name,
    description: event.description ?? `Fight card de ${event.name}.`,
    openGraph: {
      title: event.name,
      description: event.description ?? undefined,
      images: image ? [image] : undefined,
    },
  };
}

export default async function EventPage({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  const event = await getEvent(slug);
  if (!event) notFound();

  const location = [event.venue, event.city].filter(Boolean).join(", ");
  const live = Boolean(event.streamUrl) && isTodayInLisbon(event.date);
  // Main event (highest order) first for the visual card.
  const fights = [...event.fights].reverse();
  const hasResults = event.status === "Completed";

  return (
    <main className="mx-auto max-w-6xl px-4 py-12">
      <div className="grid gap-8 lg:grid-cols-[280px_1fr]">
        <div className="lg:sticky lg:top-24 lg:self-start">
          <Poster src={event.posterUrl} alt={`Cartaz de ${event.name}`} />
        </div>

        <div>
          <div className="flex flex-wrap items-center gap-2">
            {event.status === "Cancelled" && (
              <Badge variant="danger">{eventStatusLabel(event.status)}</Badge>
            )}
            {event.status === "Completed" && (
              <Badge variant="muted">{eventStatusLabel(event.status)}</Badge>
            )}
          </div>
          <h1 className="mt-2 font-heading text-3xl font-black tracking-tight sm:text-4xl">
            {event.name}
          </h1>
          <p className="mt-2 text-muted-foreground">
            {formatDateTime(event.date)}
            {location && <> · {location}</>}
          </p>
          {event.description && (
            <p className="mt-4 max-w-2xl text-muted-foreground">{event.description}</p>
          )}

          <div className="mt-5 flex flex-wrap gap-3">
            {live && (
              <a
                href={event.streamUrl ?? "#"}
                target="_blank"
                rel="noopener noreferrer"
                className="rounded-lg bg-accent px-4 py-2 font-heading text-sm font-bold text-accent-foreground transition-opacity hover:opacity-90"
              >
                Ver em direto
              </a>
            )}
            {event.ticketsUrl && (
              <a
                href={event.ticketsUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="rounded-lg bg-primary px-4 py-2 font-heading text-sm font-bold text-primary-foreground transition-opacity hover:opacity-90"
              >
                Bilhetes
              </a>
            )}
            {hasResults && (
              <Link
                href={`/events/${event.slug}/results`}
                className="rounded-lg border border-border px-4 py-2 font-heading text-sm font-bold transition-colors hover:bg-secondary"
              >
                Resultados
              </Link>
            )}
          </div>

          <h2 className="mb-4 mt-10 font-heading text-2xl font-bold">Fight card</h2>
          {fights.length > 0 ? (
            <div className="grid gap-4">
              {fights.map((fight) => (
                <FightCard key={fight.order} fight={fight} />
              ))}
            </div>
          ) : (
            <p className="text-muted-foreground">Fight card a anunciar.</p>
          )}
        </div>
      </div>
    </main>
  );
}
