import type { Metadata } from "next";
import { EventCard } from "@/components/event-card";
import { getEvents } from "@/lib/api";

export const revalidate = 3600;

export const metadata: Metadata = {
  title: "Eventos",
  description: "Próximos e anteriores eventos de desportos de combate do SFC.",
};

export default async function EventsPage() {
  const list = await getEvents();
  const upcoming = list?.upcoming ?? [];
  const past = list?.past ?? [];

  return (
    <main className="mx-auto max-w-6xl px-4 py-12">
      <h1 className="mb-8 font-heading text-3xl font-black tracking-tight">Eventos</h1>

      <section className="mb-12">
        <h2 className="mb-5 font-heading text-xl font-bold text-muted-foreground">
          Próximos
        </h2>
        {upcoming.length > 0 ? (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {upcoming.map((event) => (
              <EventCard key={event.slug} event={event} />
            ))}
          </div>
        ) : (
          <p className="text-muted-foreground">Sem eventos agendados.</p>
        )}
      </section>

      {past.length > 0 && (
        <section>
          <h2 className="mb-5 font-heading text-xl font-bold text-muted-foreground">
            Anteriores
          </h2>
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {past.map((event) => (
              <EventCard key={event.slug} event={event} />
            ))}
          </div>
        </section>
      )}
    </main>
  );
}
