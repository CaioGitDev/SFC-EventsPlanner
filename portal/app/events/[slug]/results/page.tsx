import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ResultLine } from "@/components/result-line";
import { WeighInTable } from "@/components/weigh-in-table";
import { getEvent, getEventResults, getEventWeighIns } from "@/lib/api";
import { formatDate } from "@/lib/format";

export const revalidate = 3600;
export const dynamicParams = true;

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string }>;
}): Promise<Metadata> {
  const { slug } = await params;
  const event = await getEvent(slug);
  if (!event) return { title: "Evento não encontrado" };
  return {
    title: `Resultados · ${event.name}`,
    description: `Resultados e pesagens de ${event.name}.`,
  };
}

export default async function ResultsPage({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  const [event, results, weighIns] = await Promise.all([
    getEvent(slug),
    getEventResults(slug),
    getEventWeighIns(slug),
  ]);
  if (!event) notFound();

  return (
    <main className="mx-auto max-w-4xl px-4 py-12">
      <Link
        href={`/events/${event.slug}`}
        className="text-sm text-muted-foreground hover:text-foreground"
      >
        ← {event.name}
      </Link>
      <h1 className="mt-2 font-heading text-3xl font-black tracking-tight">Resultados</h1>
      <p className="mt-1 text-muted-foreground">{formatDate(event.date)}</p>

      <section className="mt-8">
        {results && results.length > 0 ? (
          <div className="grid gap-3">
            {results.map((row) => (
              <ResultLine key={row.order} row={row} />
            ))}
          </div>
        ) : (
          <p className="text-muted-foreground">Sem resultados publicados.</p>
        )}
      </section>

      {weighIns && weighIns.length > 0 && (
        <section className="mt-12">
          <h2 className="mb-4 font-heading text-2xl font-bold">Pesagem</h2>
          <WeighInTable rows={weighIns} />
        </section>
      )}
    </main>
  );
}
