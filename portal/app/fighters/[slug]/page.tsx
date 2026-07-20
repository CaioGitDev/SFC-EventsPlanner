import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { AthleteAvatar } from "@/components/athlete-avatar";
import { Badge } from "@/components/badge";
import { RecordBadge } from "@/components/record-badge";
import { getFighter } from "@/lib/api";
import { flagFor } from "@/lib/flags";
import { formatDate } from "@/lib/format";
import { disciplineLabel } from "@/lib/labels";

export const revalidate = 3600;
export const dynamicParams = true;

// No "list all fighters" endpoint — profiles are generated fully on-demand (ISR).
export function generateStaticParams() {
  return [];
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string }>;
}): Promise<Metadata> {
  const { slug } = await params;
  const fighter = await getFighter(slug);
  if (!fighter) return { title: "Atleta não encontrado" };

  const title = fighter.nickname
    ? `${fighter.name} “${fighter.nickname}”`
    : fighter.name;
  return {
    title,
    description: `${fighter.name} — cartel ${fighter.record}, ${disciplineLabel(fighter.discipline)}.`,
    openGraph: {
      title,
      images: fighter.photoUrl ? [fighter.photoUrl] : undefined,
    },
  };
}

export default async function FighterPage({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  const fighter = await getFighter(slug);
  if (!fighter) notFound();

  const flag = flagFor(fighter.nationality);
  const statusLabel = fighter.status === "Professional" ? "Profissional" : "Amador";

  return (
    <main className="mx-auto max-w-4xl px-4 py-12">
      <div className="flex flex-col items-center gap-6 sm:flex-row sm:items-start">
        <AthleteAvatar
          src={fighter.photoUrl}
          name={fighter.name}
          className="size-32 text-3xl ring-2 ring-primary sm:size-40"
        />
        <div className="text-center sm:text-left">
          <h1 className="font-heading text-3xl font-black tracking-tight sm:text-4xl">
            {flag && <span className="mr-2">{flag}</span>}
            {fighter.name}
          </h1>
          {fighter.nickname && (
            <p className="mt-1 text-lg text-muted-foreground">“{fighter.nickname}”</p>
          )}
          <div className="mt-3 flex flex-wrap items-center justify-center gap-2 sm:justify-start">
            <RecordBadge record={fighter.record} className="text-sm" />
            <Badge variant="muted">{disciplineLabel(fighter.discipline)}</Badge>
            <Badge variant="muted">{statusLabel}</Badge>
            {fighter.winsByKo > 0 && (
              <span className="text-sm text-muted-foreground">
                {fighter.winsByKo} por KO/TKO
              </span>
            )}
          </div>
          <dl className="mt-3 flex flex-wrap gap-x-6 gap-y-1 text-sm text-muted-foreground">
            <div className="flex gap-1">
              <dt>Idade:</dt>
              <dd className="text-foreground">{fighter.age}</dd>
            </div>
            {fighter.clubName && (
              <div className="flex gap-1">
                <dt>Clube:</dt>
                <dd className="text-foreground">{fighter.clubName}</dd>
              </div>
            )}
          </dl>
        </div>
      </div>

      {fighter.nextFight && (
        <section className="mt-10">
          <h2 className="mb-3 font-heading text-xl font-bold">Próximo combate</h2>
          <Link
            href={`/events/${fighter.nextFight.eventSlug}`}
            className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-primary/40 bg-card/50 p-4 transition-colors hover:border-primary"
          >
            <div>
              <p className="font-heading font-bold">
                vs {fighter.nextFight.opponentName}
              </p>
              <p className="text-sm text-muted-foreground">
                {fighter.nextFight.eventName}
              </p>
            </div>
            <span className="text-sm text-muted-foreground">
              {formatDate(fighter.nextFight.eventDate)}
            </span>
          </Link>
        </section>
      )}

      <section className="mt-10">
        <h2 className="mb-3 font-heading text-xl font-bold">Últimos combates</h2>
        {fighter.lastFights.length > 0 ? (
          <div className="grid gap-2">
            {fighter.lastFights.map((row, i) => (
              <div
                key={`${row.eventSlug}-${i}`}
                className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border/60 bg-card/40 p-4"
              >
                <div>
                  <p className="font-medium">
                    vs{" "}
                    {row.opponentSlug ? (
                      <Link
                        href={`/fighters/${row.opponentSlug}`}
                        className="hover:underline"
                      >
                        {row.opponentName}
                      </Link>
                    ) : (
                      row.opponentName
                    )}
                  </p>
                  <Link
                    href={`/events/${row.eventSlug}`}
                    className="text-sm text-muted-foreground hover:underline"
                  >
                    {row.eventName}
                  </Link>
                </div>
                <span className="font-mono text-sm text-primary">{row.summary}</span>
              </div>
            ))}
          </div>
        ) : (
          <p className="text-muted-foreground">Sem combates registados na plataforma.</p>
        )}
      </section>
    </main>
  );
}
