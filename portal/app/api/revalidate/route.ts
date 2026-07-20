import { revalidateTag } from "next/cache";
import type { NextRequest } from "next/server";

// On-demand ISR: the .NET backoffice's PortalRevalidator POSTs { reason, eventSlug }
// with an x-revalidate-secret header whenever public content changes. Fails closed —
// no configured secret means no revalidation (the backoffice tolerates the failure).
export async function POST(req: NextRequest) {
  const secret = process.env.PORTAL_REVALIDATE_SECRET;
  if (!secret || req.headers.get("x-revalidate-secret") !== secret) {
    return new Response("Unauthorized", { status: 401 });
  }

  let eventSlug: unknown;
  let reason: unknown;
  try {
    ({ eventSlug, reason } = await req.json());
  } catch {
    // Empty/invalid body still triggers a broad revalidation below.
  }

  // expire: 0 — a backoffice webhook wants the change reflected promptly, not
  // stale-while-revalidate (the recommended pattern for external callers).
  const now = { expire: 0 };
  revalidateTag("events", now);
  revalidateTag("fighters", now);
  if (typeof eventSlug === "string" && eventSlug.length > 0) {
    revalidateTag(`event:${eventSlug}`, now);
  }

  return Response.json({
    revalidated: true,
    reason: typeof reason === "string" ? reason : null,
  });
}
