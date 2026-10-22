/** Severity (0–1) → Tailwind dot colour, shared by readiness and JD gap lists. */
export function severityColor(severity: number): string {
  if (severity >= 0.5) return "bg-red-500";
  if (severity >= 0.25) return "bg-yellow-400";
  return "bg-green-500";
}
