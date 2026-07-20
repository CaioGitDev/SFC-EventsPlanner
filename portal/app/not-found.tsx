import Link from "next/link";

export default function NotFound() {
  return (
    <main className="mx-auto grid max-w-6xl place-items-center px-4 py-24 text-center">
      <p className="font-heading text-6xl font-black text-primary">404</p>
      <h1 className="mt-4 font-heading text-2xl font-bold">Página não encontrada</h1>
      <p className="mt-2 text-muted-foreground">
        O evento ou atleta que procura não existe ou não está disponível.
      </p>
      <Link
        href="/"
        className="mt-6 rounded-lg bg-primary px-5 py-3 font-heading font-bold text-primary-foreground transition-opacity hover:opacity-90"
      >
        Voltar ao início
      </Link>
    </main>
  );
}
