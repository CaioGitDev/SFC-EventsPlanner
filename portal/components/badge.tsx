import { cn } from "@/lib/utils";

type Variant = "default" | "title" | "muted" | "danger" | "accent";

const styles: Record<Variant, string> = {
  default: "bg-primary/15 text-primary",
  title: "bg-chart-3/15 text-chart-3",
  muted: "bg-secondary text-muted-foreground",
  danger: "bg-primary/20 text-primary",
  accent: "bg-accent/20 text-accent",
};

export function Badge({
  children,
  variant = "default",
  className,
}: {
  children: React.ReactNode;
  variant?: Variant;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold uppercase tracking-wide",
        styles[variant],
        className,
      )}
    >
      {children}
    </span>
  );
}
