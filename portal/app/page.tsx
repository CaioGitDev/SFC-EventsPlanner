import Link from "next/link";
import { EventCard } from "@/components/event-card";
import { EventHero } from "@/components/event-hero";
import { ResultLine } from "@/components/result-line";
import { getEventResults, getEvents, getNextEvent } from "@/lib/api";

export const revalidate = 3600;

export default async function Home() {
  const [next, list] = await Promise.all([getNextEvent(), getEvents()]);
  const upcoming = (list?.upcoming ?? []).filter((e) => e.slug !== next?.slug);
  const past = list?.past ?? [];

  const latestPast = past[0];
  const latestResults = latestPast ? await getEventResults(latestPast.slug) : null;
  // Reverse first: the API orders by Order ascending (opener → main event), and the
  // homepage highlight should lead with the headline fights, not the preliminaries.
  const decidedResults = [...(latestResults ?? [])]
    .reverse()
    .filter((r) => r.result != null)
    .slice(0, 5);

  return (
    <main>
      {next ? (
        <EventHero event={next} />
      ) : (
        <section className="border-b border-border/60">
          <div className="mx-auto max-w-6xl px-4 py-20 text-center">
            <h1 className="font-heading text-4xl font-black tracking-tight sm:text-5xl">
              SFC
            </h1>
            <p className="mt-3 text-muted-foreground">
              Sem eventos agendados de momento. Volte em breve.
            </p>
          </div>
        </section>
      )}

      {upcoming.length > 0 && (
        <section className="mx-auto max-w-6xl px-4 py-14">
          <h2 className="mb-6 font-heading text-2xl font-bold">Próximos eventos</h2>
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {upcoming.map((event) => (
              <EventCard key={event.slug} event={event} />
            ))}
          </div>
        </section>
      )}

      {latestPast && decidedResults.length > 0 && (
        <section className="mx-auto max-w-6xl px-4 py-14">
          <div className="mb-6 flex items-end justify-between gap-4">
            <div>
              <h2 className="font-heading text-2xl font-bold">Últimos resultados</h2>
              <p className="text-sm text-muted-foreground">{latestPast.name}</p>
            </div>
            <Link
              href={`/events/${latestPast.slug}/results`}
              className="shrink-0 text-sm font-medium text-primary hover:underline"
            >
              Ver todos
            </Link>
          </div>
          <div className="grid gap-3">
            {decidedResults.map((row) => (
              <ResultLine key={row.order} row={row} />
            ))}
          </div>
        </section>
      )}

      {past.length > 0 && (
        <section className="mx-auto max-w-6xl px-4 pb-16">
          <div className="rounded-xl border border-border/60 bg-card/40 p-6 text-center">
            <Link
              href="/events"
              className="font-heading font-bold text-primary hover:underline"
            >
              Ver todos os eventos →
            </Link>
          </div>
        </section>
      )}
    </main>
  );
}
