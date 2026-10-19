// Shared placeholder for admin sections filled by follow-up issues (#103/#104/#106/#107/#108).
export default function AdminSectionPlaceholder({
  title,
  issue,
}: {
  title: string;
  issue: number;
}) {
  return (
    <section>
      <h1 className="text-2xl font-heading font-bold text-text-heading">{title}</h1>
      <p className="text-sm text-text-muted mt-1">
        This section is part of{" "}
        <a
          href={`https://github.com/VoxMentor/VoxMentor/issues/${issue}`}
          target="_blank"
          rel="noopener noreferrer"
          className="text-primary hover:underline"
        >
          Issue #{issue}
        </a>
        .
      </p>
      <div className="card-flat mt-8">
        <div className="space-y-5" aria-hidden="true">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="space-y-2">
              <div className="h-4 w-40 bg-border rounded animate-pulse" />
              <div className="h-3 w-64 bg-border rounded animate-pulse" />
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
