import Link from "next/link";

export function SiteHeader() {
  return (
    <header className="sticky top-0 z-40 border-b border-border/60 bg-background/80 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4">
        <Link href="/" className="flex items-center gap-2">
          <span className="grid size-8 place-items-center rounded bg-primary font-heading text-lg font-black text-primary-foreground">
            S
          </span>
          <span className="font-heading text-lg font-extrabold tracking-tight">
            SFC
          </span>
        </Link>
        <nav className="flex items-center gap-6 text-sm font-medium">
          <Link
            href="/"
            className="text-muted-foreground transition-colors hover:text-foreground"
          >
            Início
          </Link>
          <Link
            href="/events"
            className="text-muted-foreground transition-colors hover:text-foreground"
          >
            Eventos
          </Link>
        </nav>
      </div>
    </header>
  );
}
