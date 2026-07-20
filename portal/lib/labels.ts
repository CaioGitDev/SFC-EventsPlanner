import { formatKg } from "@/lib/format";
import type {
  Billing,
  EventStatus,
  FightStatus,
  ResultMethod,
} from "@/lib/types";

// pt-PT labels for the API's raw English enum codes (sfc-contexto vocabulary).

export function disciplineLabel(discipline: string): string {
  switch (discipline) {
    case "MuayThai":
      return "Muay Thai";
    case "Boxing":
      return "Boxe";
    case "Mma":
      return "MMA";
    default:
      return discipline; // Kickboxing, K1
  }
}

export function billingLabel(billing: Billing): string {
  switch (billing) {
    case "Main":
      return "Combate principal";
    case "CoMain":
      return "Co-main";
    default:
      return "Card";
  }
}

export function methodLabel(method: ResultMethod): string {
  switch (method) {
    case "Ko":
      return "KO";
    case "Tko":
      return "TKO";
    case "UnanimousDecision":
      return "Decisão unânime";
    case "SplitDecision":
      return "Decisão dividida";
    case "MajorityDecision":
      return "Decisão por maioria";
    case "Draw":
      return "Empate";
    case "NoContest":
      return "No contest";
    case "Disqualification":
      return "Desqualificação";
    case "Forfeit":
      return "Desistência";
  }
}

export function eventStatusLabel(status: EventStatus): string {
  switch (status) {
    case "Published":
      return "Publicado";
    case "Completed":
      return "Concluído";
    case "Cancelled":
      return "Cancelado";
    case "Draft":
      return "Rascunho";
  }
}

export function fightStatusLabel(status: FightStatus): string {
  switch (status) {
    case "Scheduled":
      return "Agendado";
    case "Completed":
      return "Concluído";
    case "Cancelled":
      return "Cancelado";
    case "NoContest":
      return "No contest";
  }
}

/** Weight class label: explicit class, or "XX kg (peso combinado)" for a catchweight. */
export function weightLabel(
  weightClass: string | null,
  catchweightKg: number | null,
): string {
  if (weightClass) return weightClass;
  if (catchweightKg != null) return `${formatKg(catchweightKg)} kg (peso combinado)`;
  return "—";
}
