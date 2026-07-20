"use client";

import { useEffect, useState } from "react";

interface Remaining {
  days: number;
  hours: number;
  mins: number;
  secs: number;
  done: boolean;
}

// The naive ISO is Lisbon wall-clock; interpreting it in the visitor's local time
// (new Date) is correct for the Portuguese audience and fine as a cosmetic countdown.
function remaining(targetMs: number): Remaining {
  const ms = Math.max(0, targetMs - Date.now());
  const total = Math.floor(ms / 1000);
  return {
    days: Math.floor(total / 86400),
    hours: Math.floor((total % 86400) / 3600),
    mins: Math.floor((total % 3600) / 60),
    secs: total % 60,
    done: ms === 0,
  };
}

export function Countdown({ targetIso }: { targetIso: string }) {
  const target = new Date(targetIso).getTime();
  const [mounted, setMounted] = useState(false);
  const [time, setTime] = useState(() => remaining(target));

  useEffect(() => {
    setMounted(true);
    setTime(remaining(target));
    const id = setInterval(() => setTime(remaining(target)), 1000);
    return () => clearInterval(id);
  }, [target]);

  // Reserve height until mounted to avoid a hydration mismatch.
  if (!mounted) return <div className="h-[72px]" aria-hidden />;

  if (time.done) {
    return (
      <p className="font-heading text-xl font-black uppercase text-primary">
        A decorrer agora
      </p>
    );
  }

  const cells: [number, string][] = [
    [time.days, "dias"],
    [time.hours, "horas"],
    [time.mins, "min"],
    [time.secs, "seg"],
  ];

  return (
    <div className="flex gap-2 sm:gap-3">
      {cells.map(([value, label]) => (
        <div
          key={label}
          className="min-w-[3.5rem] rounded-lg border border-border/60 bg-card/80 px-3 py-2 text-center"
        >
          <div className="font-heading text-2xl font-black tabular-nums sm:text-3xl">
            {String(value).padStart(2, "0")}
          </div>
          <div className="text-[0.65rem] uppercase tracking-wide text-muted-foreground">
            {label}
          </div>
        </div>
      ))}
    </div>
  );
}
