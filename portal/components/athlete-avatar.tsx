import Image from "next/image";
import { cn } from "@/lib/utils";

function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
}

export function AthleteAvatar({
  src,
  name,
  className,
}: {
  src: string | null;
  name: string;
  className?: string;
}) {
  if (!src) {
    return (
      <div
        className={cn(
          "grid place-items-center rounded-full bg-secondary font-heading font-bold text-muted-foreground",
          className,
        )}
        aria-label={name}
      >
        {initials(name)}
      </div>
    );
  }
  return (
    <Image
      src={src}
      alt={name}
      width={192}
      height={192}
      className={cn("rounded-full object-cover", className)}
    />
  );
}
