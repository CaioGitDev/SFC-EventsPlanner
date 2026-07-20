import Image from "next/image";
import { cn } from "@/lib/utils";

export function Poster({
  src,
  alt,
  className,
}: {
  src: string | null;
  alt: string;
  className?: string;
}) {
  if (!src) {
    return (
      <div
        className={cn(
          "grid aspect-[3/4] w-full place-items-center rounded-lg border border-border/60 bg-secondary text-sm text-muted-foreground",
          className,
        )}
      >
        Sem cartaz
      </div>
    );
  }
  return (
    <Image
      src={src}
      alt={alt}
      width={600}
      height={800}
      className={cn("aspect-[3/4] w-full rounded-lg object-cover", className)}
    />
  );
}
