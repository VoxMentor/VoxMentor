#!/usr/bin/env python3
"""VoxMentor tutor retrieval + prompt evaluation (#77).

Subcommands:
  seed  -- chunk + embed scripts/eval/corpus.txt into "TextbookChunks"
            (--clean removes prior eval rows first)
  run   -- evaluate scripts/eval/golden-qa.json through the same retrieval
            path (pgvector cosine) and prompt template prod uses, then gate.

Metrics (logged for future improvement, see docs in issue #77):
  retrieval   recall@k = fraction of goldens whose expected Source appears in
              the top-k; MRR = mean 1/rank; mean cosine distance.
  citations   precision = valid [n] references / all [n] references
              (0.0 when the answer cites nothing); expected-cited boolean.
  answers     keyphrase coverage = matched / listed phrases (case-insensitive
              substring). Reported, not gated.
  latency     wall-clock for the non-streaming Ollama chat call.

Prompt decisions (#77, mirrored in TutorService.BuildPrompt):
  - single source of truth: prompts/tutor-prompt.txt (C# embeds this file);
    this script renders it identically - keep placeholder format in sync.
  - numbered [n] citations bind answers to retrieved chunks so citation
    precision is machine-checkable; unanswerable questions must say the
    excerpts lack the information rather than guess.
  - k sweep runs retrieval-only at 3/5/7; generation runs at the winner k to
    cap Ollama calls at one per golden pair.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

try:
    import psycopg
    import requests
except ImportError as exc:  # pragma: no cover
    sys.exit(f"missing dependency: {exc.name} - pip install -r scripts/requirements-eval.txt")

ROOT = Path(__file__).resolve().parents[1]
CORPUS_PATH = ROOT / "scripts" / "eval" / "corpus.txt"
GOLDEN_PATH = ROOT / "scripts" / "eval" / "golden-qa.json"
TEMPLATE_PATH = ROOT / "prompts" / "tutor-prompt.txt"
REPORT_PATH = ROOT / "scripts" / "eval" / "report.json"
APPSETTINGS_PATH = ROOT / "src" / "VoxMentor.Api" / "appsettings.Development.json"

SOURCE_PREFIX = "eval/corpus.txt#"
JOB_FILENAME = "eval/corpus.txt"
# Fixed owner marker for seeded eval rows (CodeRabbit #96): cleanup and run
# key on this JobId only, never on FileName/Source, so an admin upload named
# eval/corpus.txt is never matched.
EVAL_JOB_ID = "00000077-0077-4777-8777-000000000077"

# Mirrors TextChunker.cs (words, not tokens): 375/37 overlap, step 338.
CHUNK_SIZE_WORDS = 375
CHUNK_OVERLAP_WORDS = 37
EMBED_DIM = 768  # CodeEmbeddingService asserts 768 too

# Corpus slug -> Concepts.Name (concept-filtered retrieval exercises prod's
# "AND ConceptId = ..." path; requires scripts/seed-dsa-concepts.sql loaded).
SLUG_TO_CONCEPT = {
    "big-o": "Time Complexity",
    "arrays": "Arrays",
    "strings": "Strings",
    "hash-maps": "Hash Maps and Sets",
    "linked-lists": "Singly Linked Lists",
    "stacks-queues": "Stacks",
    "two-pointers": "Two Pointers",
    "sliding-window": "Sliding Window",
    "binary-search": "Binary Search",
    "sorting": "Merge Sort",
    "bst": "Binary Search Trees",
    "tree-traversals": "Tree Traversals",
    "heaps": "Heap Data Structure",
    "bfs-dfs": "Breadth-First Search (BFS)",
    "dijkstra": "Dijkstra's Algorithm",
    "greedy": "Greedy Algorithms",
    "dynamic-programming": "Memoization",
}


# ---------------------------------------------------------------- config

def ollama_base() -> str:
    return os.environ.get("OLLAMA_BASE_URL", "http://localhost:11434").rstrip("/")


def embed_model() -> str:
    return os.environ.get("EMBED_MODEL", "nomic-embed-text")


def chat_model() -> str:
    return os.environ.get("CHAT_MODEL", "llama3.2:3b")


def db_dsn() -> str:
    if dsn := os.environ.get("VOXMENTOR_DB"):
        return dsn
    if not APPSETTINGS_PATH.exists():
        sys.exit("set VOXMENTOR_DB or run from the repo (appsettings.Development.json missing)")
    conn = json.loads(APPSETTINGS_PATH.read_text(encoding="utf-8"))["ConnectionStrings"]["DefaultConnection"]
    return ado_to_libpq(conn)


def ado_to_libpq(ado: str) -> str:
    """Convert ADO.NET key/value pairs (Host=...;Ssl Mode=Require) to libpq.
    Keys match case-insensitively (Npgsql accepts 'Ssl Mode'/'SSL Mode',
    'Username'/'User Id'). Trust Server Certificate is dropped:
    sslmode=require already skips root CA verification (only verify-*
    modes validate certificates)."""
    kv = {}
    for part in ado.split(";"):
        if "=" in part:
            k, v = part.split("=", 1)
            kv[k.strip().lower()] = v
    mapping = {
        "host": "host", "port": "port", "database": "dbname",
        "username": "user", "user id": "user", "password": "password",
        "ssl mode": "sslmode",
    }
    outkv = {}
    for src, dst in mapping.items():
        if src in kv:
            value = kv[src].strip()
            if dst == "sslmode":
                value = value.lower()
            if " " in value or "'" in value:
                value = "'" + value.replace("'", "''") + "'"
            outkv[dst] = value
    return " ".join(f"{k}={v}" for k, v in outkv.items())


def connect():
    conn = psycopg.connect(db_dsn(), row_factory=psycopg.rows.dict_row, connect_timeout=15)
    # CodeRabbit #96: mirror prod's TutorService retrieval session settings so
    # eval and prod see the same HNSW behavior. Session-scoped on this
    # dedicated connection; ef_search is set per-k in cmd_run. Needs
    # pgvector >= 0.8 — fail fast with a clear message instead of a raw error.
    try:
        with conn.cursor() as cur:
            cur.execute("SET hnsw.iterative_scan = 'strict_order'")
    except psycopg.Error as exc:
        conn.close()
        sys.exit(f"pgvector too old for hnsw.iterative_scan (need >= 0.8): {exc}")
    return conn


# ------------------------------------------------------------- corpus/chunk

def parse_corpus(text: str) -> list[tuple[str, str]]:
    sections: list[tuple[str, str]] = []
    slug, buf = None, []
    for line in text.splitlines():
        if line.startswith("## "):
            if slug is not None:
                sections.append((slug, "\n".join(buf)))
            slug, buf = line[3:].strip(), []
        elif slug is not None:
            buf.append(line)
    if slug is not None:
        sections.append((slug, "\n".join(buf)))
    if not sections:
        sys.exit(f"no '## <slug>' sections found in {CORPUS_PATH}")
    return sections


def chunk_words(text: str) -> list[str]:
    """Mirror of TextChunker.Chunk: split on whitespace, 375-word windows,
    37-word overlap (step 338), normalized single-space join."""
    words = text.split()
    if not words:
        return []
    step = CHUNK_SIZE_WORDS - CHUNK_OVERLAP_WORDS
    out, start = [], 0
    while start < len(words):
        count = min(CHUNK_SIZE_WORDS, len(words) - start)
        out.append(" ".join(words[start:start + count]))
        if start + count >= len(words):
            break
        start += step
    return out


# ---------------------------------------------------------------- ollama

def embed(text: str) -> list[float]:
    try:
        resp = requests.post(
            f"{ollama_base()}/api/embed",
            json={"model": embed_model(), "input": text[:8000]},
            timeout=60,
        )
        resp.raise_for_status()
        vec = resp.json()["embeddings"][0]
    except (requests.RequestException, KeyError, IndexError) as exc:
        sys.exit(f"embedding failed via {ollama_base()} - is the '{embed_model()}' model up? ({exc})")
    if len(vec) != EMBED_DIM or not all(isinstance(v, (int, float)) for v in vec):
        sys.exit(f"embedding has wrong dimension ({len(vec)}), expected {EMBED_DIM}")
    return vec


def chat(prompt: str) -> tuple[str, float]:
    t0 = time.perf_counter()
    try:
        resp = requests.post(
            f"{ollama_base()}/api/chat",
            json={"model": chat_model(), "messages": [{"role": "user", "content": prompt}], "stream": False},
            timeout=300,
        )
        resp.raise_for_status()
        data = resp.json()["message"]["content"]
    except (requests.RequestException, KeyError) as exc:
        sys.exit(f"chat failed via {ollama_base()} - is the '{chat_model()}' model up? ({exc})")
    return data, time.perf_counter() - t0


def check_ollama(need_chat: bool) -> None:
    try:
        resp = requests.get(f"{ollama_base()}/api/tags", timeout=5)
        resp.raise_for_status()
    except requests.RequestException as exc:
        sys.exit(f"Ollama unreachable at {ollama_base()} ({exc}). Start it: ollama serve")
    have = {m["name"] for m in resp.json().get("models", [])}
    needed = [embed_model()] + ([chat_model()] if need_chat else [])
    for model in needed:
        if model not in have and f"{model}:latest" not in have:
            sys.exit(f"model '{model}' not pulled - ollama pull {model}")


# ----------------------------------------------------------------- prompt

def _escape_untrusted(value: str) -> str:
    # mirror of TutorService.EscapeUntrusted: a literal "</" would close the
    # <excerpts>/<question> delimiters early (CWE-1427, CodeRabbit #96)
    return value.replace("</", "<\\/")


def render_prompt(question: str, chunks: list[dict]) -> str:
    """Mirror of TutorService.BuildPrompt. chunks: [{content, source}]."""
    template = TEMPLATE_PATH.read_text(encoding="utf-8").replace("\r\n", "\n").rstrip("\n")
    if chunks:
        block = "<excerpts>\nExcerpts:\n" + "\n".join(
            f"[{i}] {_escape_untrusted(c['content'])}\n    Source: {_escape_untrusted(c['source'])}"
            for i, c in enumerate(chunks, 1)
        ) + "\n</excerpts>"
        template = template.replace("{EXCERPTS}", block, 1)
    else:
        template = re.sub(r"\{EXCERPTS\}\n?", "", template, count=1)
    return template.replace(
        "{QUESTION}", "<question>" + _escape_untrusted(question) + "</question>", 1)


# ---------------------------------------------------------------- retrieval

def retrieve(cur, vec: list[float], k: int, concept_id: str | None) -> list[dict]:
    lit = "[" + ",".join(repr(float(v)) for v in vec) + "]"
    if concept_id:
        sql = """
            SELECT "Id", "Content", "Source",
                   "Embedding"::vector <=> %s::vector AS "Distance"
            FROM "TextbookChunks"
            WHERE "Embedding" IS NOT NULL AND "ConceptId" = %s
            ORDER BY "Embedding"::vector <=> %s::vector
            LIMIT %s
            """
        params = (lit, concept_id, lit, k)
    else:
        sql = """
            SELECT "Id", "Content", "Source",
                   "Embedding"::vector <=> %s::vector AS "Distance"
            FROM "TextbookChunks"
            WHERE "Embedding" IS NOT NULL
            ORDER BY "Embedding"::vector <=> %s::vector
            LIMIT %s
            """
        params = (lit, lit, k)
    cur.execute(sql, params)
    return cur.fetchall()


# -------------------------------------------------------------- validation

def load_and_validate() -> tuple[dict, list[dict]]:
    data = json.loads(GOLDEN_PATH.read_text(encoding="utf-8"))
    goldens = data.get("golden", [])
    if not goldens:
        sys.exit(f"no golden entries in {GOLDEN_PATH}")
    corpus_slugs = {slug for slug, _ in parse_corpus(CORPUS_PATH.read_text(encoding="utf-8"))}
    seen: set[str] = set()
    for g in goldens:
        missing = [k for k in ("id", "question", "expected_source", "expected_answer",
                               "expected_answer_keyphrases") if k not in g]
        if missing:
            sys.exit(f"golden {g.get('id', '?')}: missing keys {missing}")
        if g["id"] in seen:
            sys.exit(f"duplicate golden id {g['id']}")
        seen.add(g["id"])
        if not isinstance(g["expected_answer_keyphrases"], list) or not g["expected_answer_keyphrases"]:
            sys.exit(f"golden {g['id']}: expected_answer_keyphrases must be a non-empty list")
        source = g["expected_source"]
        if not source.startswith(SOURCE_PREFIX):
            sys.exit(f"golden {g['id']}: expected_source must start with {SOURCE_PREFIX}")
        if source[len(SOURCE_PREFIX):] not in corpus_slugs:
            sys.exit(f"golden {g['id']}: expected_source slug '{source}' not in corpus sections")
        if g.get("concept") and g["concept"] not in SLUG_TO_CONCEPT.values():
            sys.exit(f"golden {g['id']}: concept '{g['concept']}' not in SLUG_TO_CONCEPT")
    gates = data.get("gates", {})
    for key in ("min_recall_at_winner", "min_citation_precision", "max_avg_latency_s"):
        if key not in gates:
            sys.exit(f"golden-qa.json: missing gate '{key}'")
    if len(goldens) != 20:
        print(f"WARN: expected 20 golden pairs, found {len(goldens)}", file=sys.stderr)
    return gates, goldens


# ------------------------------------------------------------------- seed

def cmd_seed(args: argparse.Namespace) -> int:
    sections = parse_corpus(CORPUS_PATH.read_text(encoding="utf-8"))
    with connect() as conn, conn.cursor() as cur:
        if args.clean:
            cur.execute('DELETE FROM "TextbookChunks" WHERE "JobId" = %s', (EVAL_JOB_ID,))
            removed_chunks = cur.rowcount
            cur.execute('DELETE FROM "TextbookJobs" WHERE "Id" = %s', (EVAL_JOB_ID,))
            print(f"cleaned {removed_chunks} prior eval chunks (+{cur.rowcount} job rows)")

        cur.execute('SELECT "Id", "Name" FROM "Concepts"')
        concepts = {row["Name"]: row["Id"] for row in cur.fetchall()}

        job_id = EVAL_JOB_ID
        chunks: list[tuple[str, str, str | None]] = []  # (source, content, concept_id)
        for slug, body in sections:
            concept_name = SLUG_TO_CONCEPT.get(slug)
            concept_id = str(concepts[concept_name]) if concept_name in concepts else None
            if concept_name and concept_id is None:
                print(f"WARN: concept '{concept_name}' not in DB; chunk {slug} unfiltered")
            for piece in chunk_words(body):
                chunks.append((f"{SOURCE_PREFIX}{slug}", piece, concept_id))

        now = datetime.now(timezone.utc)
        cur.execute(
            """INSERT INTO "TextbookJobs"
               ("Id", "FileName", "ConceptId", "Status", "TotalChunks",
                "ProcessedChunks", "Error", "CreatedAt", "CompletedAt")
               VALUES (%s, %s, NULL, 'Completed', %s, %s, NULL, %s, %s)""",
            (job_id, JOB_FILENAME, len(chunks), len(chunks), now, now),
        )

        inserted = 0
        for source, content, concept_id in chunks:
            vec = embed(content)
            cur.execute(
                """INSERT INTO "TextbookChunks"
                   ("Id", "JobId", "ConceptId", "Content", "Source", "Embedding")
                   VALUES (%s, %s, %s, %s, %s, %s::vector)""",
                (str(uuid.uuid4()), job_id, concept_id, content, source,
                 "[" + ",".join(repr(float(v)) for v in vec) + "]"),
            )
            inserted += 1
    print(f"seeded {inserted} chunks from {len(sections)} sections "
          f"({sum(1 for c in chunks if c[2])} concept-tagged)")
    return 0


# -------------------------------------------------------------------- run

def cmd_run(args: argparse.Namespace) -> int:
    gates, goldens = load_and_validate()
    k_values = [int(k) for k in args.k.split(",") if k.strip()]
    if not k_values or min(k_values) < 1:
        sys.exit("--k must be a comma-separated list of positive ints")
    check_ollama(need_chat=not args.skip_generation)

    with connect() as conn, conn.cursor() as cur:
        cur.execute('SELECT count(*) AS n FROM "TextbookChunks" WHERE "JobId" = %s',
                    (EVAL_JOB_ID,))
        seeded = cur.fetchone()["n"]
        if seeded == 0:
            sys.exit(f"no eval chunks found - run: {Path(sys.argv[0]).name} seed")
        cur.execute('SELECT "Id", "Name" FROM "Concepts"')
        concept_ids = {row["Name"]: str(row["Id"]) for row in cur.fetchall()}

        # retrieval sweep: one embedding per golden, queried at every k
        rows = []
        for g in goldens:
            vec = embed(g["question"])
            concept_id = concept_ids.get(g.get("concept")) if g.get("concept") else None
            if g.get("concept") and concept_id is None:
                print(f"WARN: concept '{g['concept']}' missing in DB; running unfiltered")
            ranks = {}
            for k in k_values:
                # mirror TutorService's ef_search budget for this k (CodeRabbit #96)
                cur.execute(f"SET hnsw.ef_search = {max(40, 2 * k)}")
                hits = retrieve(cur, vec, k, concept_id)
                rank = next((i for i, h in enumerate(hits, 1) if h["Source"] == g["expected_source"]), None)
                ranks[k] = {
                    "rank": rank,
                    "hit": rank is not None,
                    "mean_distance": sum(h["Distance"] for h in hits) / len(hits) if hits else None,
                    "chunks": [{"content": h["Content"], "source": h["Source"]} for h in hits],
                }
            rows.append({"golden": g, "ranks": ranks})

    # per-k aggregates
    by_k = {}
    for k in k_values:
        hits = [r["ranks"][k]["hit"] for r in rows]
        rranks = [r["ranks"][k]["rank"] for r in rows if r["ranks"][k]["rank"]]
        dists = [r["ranks"][k]["mean_distance"] for r in rows
                 if r["ranks"][k]["mean_distance"] is not None]
        by_k[k] = {
            "recall": sum(hits) / len(hits),
            "mrr": sum(1.0 / x for x in rranks) / len(rows) if rranks else 0.0,
            "mean_distance": sum(dists) / len(dists) if dists else None,
        }
    winner = min(k_values, key=lambda k: (-by_k[k]["recall"], -by_k[k]["mrr"], k))
    print(f"\nRetrieval sweep over {len(rows)} golden pairs, corpus chunks={seeded}")
    print(f"{'k':>3}  {'recall':>7}  {'MRR':>6}  {'mean_dist':>10}")
    for k in k_values:
        d = by_k[k]
        md = f"{d['mean_distance']:.4f}" if d["mean_distance"] is not None else "-"
        print(f"{k:>3}  {d['recall']:>7.2f}  {d['mrr']:>6.3f}  {md:>10}")
    print(f"winner: k={winner}")

    # generation at winner k
    answers = []
    if args.skip_generation:
        print("generation skipped (--skip-generation)")
    else:
        for r in rows:
            g, chunks = r["golden"], r["ranks"][winner]["chunks"]
            prompt = render_prompt(g["question"], chunks)
            answer, latency = chat(prompt)
            cites = [int(m) for m in re.findall(r"\[(\d+)\]", answer)]
            valid = [c for c in cites if 1 <= c <= len(chunks)]
            cited_sources = {chunks[c - 1]["source"] for c in valid}
            phrases = g["expected_answer_keyphrases"]
            matched = [p for p in phrases if p.lower() in answer.lower()]
            answers.append({
                "id": g["id"],
                "latency_s": round(latency, 2),
                "citation_precision": (len(valid) / len(cites)) if cites else 0.0,
                "citations": len(cites),
                "expected_cited": g["expected_source"] in cited_sources,
                "keyphrase_coverage": len(matched) / len(phrases),
                "answer": answer,
            })
            print(f"  {g['id']}: k={winner} rank={r['ranks'][winner]['rank']} "
                  f"lat={latency:.1f}s cites={len(valid)}/{len(cites)} "
                  f"cov={len(matched) / len(phrases):.2f}")

    # gates
    failures = []
    max_latency_gate = (
        args.max_avg_latency_s if args.max_avg_latency_s is not None
        else gates["max_avg_latency_s"])
    recall_winner = by_k[winner]["recall"]
    if recall_winner < gates["min_recall_at_winner"]:
        failures.append(f"recall@{winner} {recall_winner:.2f} < {gates['min_recall_at_winner']}")
    skipped_gates: list[str] = []
    if answers:
        mean_precision = sum(a["citation_precision"] for a in answers) / len(answers)
        mean_latency = sum(a["latency_s"] for a in answers) / len(answers)
        p95_latency = sorted(a["latency_s"] for a in answers)[int(0.95 * (len(answers) - 1))]
        mean_cov = sum(a["keyphrase_coverage"] for a in answers) / len(answers)
        expected_cited_rate = sum(a["expected_cited"] for a in answers) / len(answers)
        if mean_precision < gates["min_citation_precision"]:
            failures.append(f"citation precision {mean_precision:.2f} < {gates['min_citation_precision']}")
        if mean_latency > max_latency_gate:
            failures.append(f"avg latency {mean_latency:.1f}s > {max_latency_gate}s")
    else:
        mean_precision = mean_latency = p95_latency = mean_cov = expected_cited_rate = None
        skipped_gates = ["min_citation_precision", "max_avg_latency_s"]

    report = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "models": {"embed": embed_model(), "chat": chat_model()},
        "corpus_chunks": seeded,
        "k_values": k_values,
        "winner_k": winner,
        "retrieval_by_k": {str(k): by_k[k] for k in k_values},
        "goldens": [
            {
                "id": r["golden"]["id"],
                "expected_source": r["golden"]["expected_source"],
                "concept": r["golden"].get("concept"),
                "ranks": {str(k): r["ranks"][k]["rank"] for k in k_values},
                **(next((a for a in answers if a["id"] == r["golden"]["id"]), {}) or {}),
            }
            for r in rows
        ],
        "aggregates": {
            "recall_at_winner": recall_winner,
            "citation_precision_mean": mean_precision,
            "keyphrase_coverage_mean": mean_cov,
            "expected_cited_rate": expected_cited_rate,
            "latency_mean_s": mean_latency,
            "latency_p95_s": p95_latency,
        },
        "gates": {
            "defined": gates,
            "max_avg_latency_s_applied": max_latency_gate,
            "skipped": skipped_gates,
            "failures": failures,
            "passed": not failures,
        },
    }
    tmp = REPORT_PATH.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(report, indent=2), encoding="utf-8")
    os.replace(tmp, REPORT_PATH)

    print("\nAggregates:")
    for key, value in report["aggregates"].items():
        print(f"  {key}: {value if value is None else round(value, 3)}")
    if failures:
        print("\nGATES FAILED:")
        for f in failures:
            print(f"  - {f}")
        print(f"report: {REPORT_PATH}")
        return 1
    if skipped_gates:
        print("\nretrieval gates passed (generation gates skipped: " + ", ".join(skipped_gates) + ")")
    else:
        print("\nall gates passed")
    print(f"report: {REPORT_PATH}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="VoxMentor tutor eval (#77)")
    sub = parser.add_subparsers(dest="cmd", required=True)
    p_seed = sub.add_parser("seed", help="chunk + embed corpus into TextbookChunks")
    p_seed.add_argument("--clean", action="store_true", help="delete prior eval rows first")
    p_run = sub.add_parser("run", help="run golden Q&A evaluation")
    p_run.add_argument("--k", default="3,5,7", help="retrieval k sweep (default 3,5,7)")
    p_run.add_argument("--skip-generation", action="store_true", help="retrieval metrics only")
    p_run.add_argument(
        "--max-avg-latency-s", type=float, default=None,
        help="override gates.max_avg_latency_s for this run - hardware knob: the "
             "JSON value (8s) is the prod GPU-class target, CPU-only boxes cannot "
             "hit it; record overrides in baseline.md",)
    args = parser.parse_args()
    if args.cmd == "seed":
        return cmd_seed(args)
    return cmd_run(args)


if __name__ == "__main__":
    sys.exit(main())
