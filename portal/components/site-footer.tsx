export function SiteFooter() {
  return (
    <footer className="mt-auto border-t border-border/60">
      <div className="mx-auto max-w-6xl px-4 py-8 text-sm text-muted-foreground">
        <p className="font-heading font-bold text-foreground">SFC EventsPlanner</p>
        <p className="mt-1">
          Desportos de combate — Muay Thai, Kickboxing, K1, Boxe e MMA.
        </p>
        <p className="mt-4 text-xs">
          © {new Date().getFullYear()} SFC. Todos os direitos reservados.
        </p>
      </div>
    </footer>
  );
}
