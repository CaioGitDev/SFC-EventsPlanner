import { cn } from "@/lib/utils";

export function RecordBadge({
  record,
  className,
}: {
  record: string;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-block rounded bg-secondary px-2 py-0.5 font-mono text-xs font-medium",
        className,
      )}
      title="Vitórias-Derrotas-Empates"
    >
      {record}
    </span>
  );
}
