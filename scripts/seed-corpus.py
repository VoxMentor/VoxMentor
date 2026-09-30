#!/usr/bin/env python3
"""VoxMentor seed corpus (issue #75): map DSA articles to the 50 concepts and
ingest them through POST /api/v1/admin/textbook/upload.

Why one file per concept
  The upload endpoint stamps a single conceptId on the job, and IngestionJob
  copies it onto every chunk it stores (IngestionJob.cs:105). Per-chunk concept
  mapping therefore has to happen here, before upload: group the text by
  concept, then upload one .txt per concept.

Where the text comes from
  scripts/corpus/articles.json - curated GeeksforGeeks articles, one list per
  concept, so the mapping is curated rather than guessed. No copyrighted text is
  committed: articles are fetched at run time into a temp dir.
  Optional extra input: .txt files in scripts/corpus/source/ (gitignored), e.g.
  a CTCI PDF converted to text that you are licensed to use. Those carry no
  concept label, so they go through keyword matching + stdlib TF-IDF to pick
  the nearest concept.

Modes
  plan    map the corpus, report per-concept chunk counts, touch nothing
  seed    plan, then upload one file per concept and poll each job
  verify  check the acceptance criteria against the live database

Prereqs
  pip install -r scripts/requirements-seed.txt
  Stack up (API + Postgres + Hangfire + Ollama), concepts loaded
  (scripts/seed-dsa-concepts.sql). Uploads need ContentAdmin or SuperAdmin;
  register only grants Student, so grant the role once:

    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
    SELECT u."Id", r."Id" FROM "AspNetUsers" u, "AspNetRoles" r
    WHERE u."Email" = 'you@example.com' AND r."Name" = 'ContentAdmin';

  Then:
    VOXMENTOR_ADMIN_EMAIL=... VOXMENTOR_ADMIN_PASSWORD=... \
      python scripts/seed-corpus.py seed
"""
from __future__ import annotations

import argparse
import json
import math
import os
import re
import subprocess
import sys
import tempfile
import time
import uuid
from collections import Counter
from pathlib import Path

try:
    import requests
    from bs4 import BeautifulSoup
except ImportError as exc:  # pragma: no cover
    sys.exit(f"missing dependency: {exc.name} - pip install -r scripts/requirements-seed.txt")

ROOT = Path(__file__).resolve().parents[1]
ARTICLES_PATH = ROOT / "scripts" / "corpus" / "articles.json"
SOURCE_DIR = ROOT / "scripts" / "corpus" / "source"

# TextbookChunks.Source is the uploaded file name, and it is the only handle the
# upload API gives us: unlike eval-tutor.py (which seeds straight into the DB and
# so owns a fixed EVAL_JOB_ID) the job ids here are server-generated. --clean and
# verify therefore match on this prefix - kept distinctive so a hand-made admin
# upload is very unlikely to collide.
SOURCE_PREFIX = "seed-corpus-"

USER_AGENT = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
              "(KHTML, like Gecko) Chrome/124.0 Safari/537.36")

# Mirrors TextChunker.cs (words, not tokens): 375-word window, 37-word overlap.
CHUNK_SIZE_WORDS = 375
CHUNK_OVERLAP_WORDS = 37
CHUNK_STEP = CHUNK_SIZE_WORDS - CHUNK_OVERLAP_WORDS

MAX_FILE_BYTES = 20 * 1024 * 1024  # UploadTextbookValidator.MaxFileSizeBytes

# concept name -> search terms, for the auto-mapper used on unlabelled local
# files. Curated articles carry their concept in articles.json and never reach
# this. Keys must match Concepts.Name in scripts/seed-dsa-concepts.sql.
CONCEPT_KEYWORDS = {
    "Variables and Data Types": ["variable", "data type", "primitive", "storage", "floating point"],
    "Control Flow": ["if else", "loop", "while", "for loop", "switch statement", "control flow", "iteration"],
    "Functions": ["function", "parameter", "argument", "return value", "call stack", "prototype"],
    "Time Complexity": ["time complexity", "big o", "big-o", "o(n log n)", "o(n^2)", "o(1)", "asymptotic"],
    "Space Complexity": ["space complexity", "auxiliary space", "extra space", "in-place", "memory usage"],
    "Arrays": ["array", "contiguous", "subarray", "two sum", "maximum subarray", "kadane", "indexing"],
    "Strings": ["string", "substring", "character array", "palindrome", "char array", "concatenat"],
    "Hash Maps and Sets": ["hashmap", "hash map", "hashing", "dictionary", "frequency count", "collision", "bucket", "set data structure"],
    "Singly Linked Lists": ["singly", "linked list", "next pointer", "reverse a linked list", "node class"],
    "Doubly Linked Lists": ["doubly", "prev pointer", "previous node", "both directions"],
    "Stacks": ["stack", "lifo", "push", "pop", "parenthes", "undo", "balanced bracket"],
    "Queues": ["queue", "fifo", "enqueue", "dequeue", "circular queue"],
    "Deque": ["deque", "double-ended", "double ended", "push front", "pop front", "sliding window maximum"],
    "Recursion": ["recursion", "recursive", "base case", "call stack", "stack overflow"],
    "Binary Search": ["binary search", "sorted array", "lower bound", "upper bound", "mid index"],
    "Two Pointers": ["two pointer", "two-pointer", "two pointers", "converging", "palindrome"],
    "Sliding Window": ["sliding window", "window size", "max window", "subarray of size"],
    "Prefix Sum": ["prefix sum", "prefix array", "cumulative sum", "running total"],
    "Bubble Sort": ["bubble sort", "adjacent swap"],
    "Selection Sort": ["selection sort", "select the minimum", "minimum element in the unsorted"],
    "Insertion Sort": ["insertion sort", "shift elements", "key element"],
    "Merge Sort": ["merge sort", "divide and conquer", "merging two sorted"],
    "Quick Sort": ["quick sort", "quicksort", "pivot", "partition"],
    "Counting Sort": ["counting sort", "frequency array", "bounded integer", "o(n + k)"],
    "Binary Trees": ["binary tree", "subtree", "root node", "diameter", "height of the tree", "complete tree"],
    "Binary Search Trees": ["binary search tree", "bst", "left subtree", "right subtree", "inorder successor",
                            "predecessor", "successor", "balanced binary tree", "bst property"],
    "Tree Traversals": ["inorder", "preorder", "postorder", "traversal", "level order"],
    "Heap Data Structure": ["heap", "complete binary tree", "sift", "heapify", "min-heap", "max-heap"],
    "Priority Queues": ["priority queue", "priority", "top k", "scheduling"],
    "Trie": ["trie", "prefix tree", "autocomplete", "dictionary tree", "insert word"],
    "Graph Representations": ["adjacency list", "adjacency matrix", "graph", "vertex", "undirected graph", "directed graph"],
    "Breadth-First Search (BFS)": ["bfs", "breadth-first", "breadth first", "level by level"],
    "Depth-First Search (DFS)": ["dfs", "depth-first", "depth first", "backtracking traversal"],
    "Topological Sort": ["topological", "kahn", "dependency ordering", "indegree", "dag"],
    "Cycle Detection": ["cycle", "visited array", "recursion stack", "circular"],
    "Union-Find": ["union-find", "disjoint set", "find parent", "path compression", "union by rank"],
    "Dijkstra's Algorithm": ["dijkstra", "shortest path", "non-negative", "priority queue"],
    "Bellman-Ford Algorithm": ["bellman-ford", "bellman", "negative weight", "negative cycle", "v-1 edges"],
    "Floyd-Warshall Algorithm": ["floyd-warshall", "floyd", "warshall", "all-pairs", "distance matrix"],
    "Kruskal's Algorithm": ["kruskal", "spanning tree", "sort the edges", "minimum spanning"],
    "Prim's Algorithm": ["prim's", "spanning tree", "minimum weight edge"],
    "Weighted Graphs": ["weighted", "weight", "cost", "shortest path"],
    "Shortest Path Algorithms": ["shortest path", "single source", "all pairs", "dijkstra", "bellman", "floyd"],
    "Minimum Spanning Tree": ["minimum spanning tree", "mst", "spanning tree", "minimum weight"],
    "Backtracking": ["backtracking", "backtrack", "n-queens", "queens", "pruning", "permutation", "subset", "unchoose"],
    "Memoization": ["memoization", "memoize", "top-down", "overlapping subproblems", "cache the result"],
    "Tabulation": ["tabulation", "bottom-up", "iterative dynamic programming", "dp table"],
    "Knapsack Problem": ["knapsack", "0/1 knapsack", "weight and value", "maximum value"],
    "Longest Common Subsequence": ["longest common subsequence", "lcs", "common subsequence", "edit distance", "supersequence"],
    "Greedy Algorithms": ["greedy", "greedy choice", "locally optimal", "activity selection", "coin change"],
}

STOPWORDS = frozenset("""
a an the and or but if then this that these those is are was were be been being of to in on for with
as by from at it its into we you your our they their he she his her not no can will would should could
what which who whom how when where why do does did done have has had having use used using also such
than too very just only about over under more most some any all each other another own same so
""".split())


# ----------------------------------------------------------------- chunking

def chunk_words(text: str) -> list[str]:
    """Mirror of TextChunker.Chunk so plan-mode counts match what the server stores."""
    words = text.split()
    if not words:
        return []
    out: list[str] = []
    start = 0
    while start < len(words):
        count = min(CHUNK_SIZE_WORDS, len(words) - start)
        out.append(" ".join(words[start:start + count]))
        if start + count >= len(words):
            break
        start += CHUNK_STEP
    return out


def chunk_count(words: int) -> int:
    if words <= 0:
        return 0
    if words <= CHUNK_SIZE_WORDS:
        return 1
    return 1 + -(-(words - CHUNK_SIZE_WORDS) // CHUNK_STEP)


# -------------------------------------------------------------------- fetch

def fetch_article(session: requests.Session, url: str, retries: int = 1) -> tuple[str, str] | None:
    """Return (title, body text) for an article, or None when unusable."""
    for attempt in range(retries + 1):
        try:
            resp = session.get(url, timeout=30)
        except requests.RequestException as exc:
            print(f"    ! {exc.__class__.__name__}: {url}")
            if attempt < retries:
                time.sleep(3)
                continue
            return None
        if resp.status_code != 200:
            return None  # hard miss; retrying a 404 will not help
        soup = BeautifulSoup(resp.text, "lxml")
        body, h1 = soup.find("div", class_="text"), soup.find("h1")
        if body and h1:
            text = body.get_text(" ", strip=True)
            if len(text.split()) >= 150:
                return h1.get_text(strip=True), text
        if attempt < retries:
            time.sleep(3)
    print(f"    ! no article body: {url}")
    return None


def slugify(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-") or uuid.uuid4().hex[:8]


# ------------------------------------------------------------- auto-mapping

def _tokens(text: str) -> Counter:
    return Counter(w for w in re.findall(r"[a-z0-9']+", text.lower())
                   if len(w) > 2 and w not in STOPWORDS)


def keyword_score(text_lower: str, terms: list[str]) -> float:
    """Alias hits per 1000 words. Cheap and high-signal."""
    if not terms:
        return 0.0
    hits = sum(text_lower.count(t) for t in terms)
    return hits / (1 + len(text_lower.split()) / 1000.0)


def tfidf_scores(text: str, profiles: dict[str, list[str]]) -> dict[str, float]:
    """Stdlib TF-IDF cosine between one document and each concept profile.

    Each profile is that concept's keywords, so this only breaks ties the alias
    scan left close. 50 short profiles - scikit-learn would be a 40 MB dependency
    for a matrix this small.
    """
    docs = [_tokens(text)] + [_tokens(" ".join(terms * 4)) for terms in profiles.values()]
    n_docs = len(docs)

    df: Counter = Counter()
    for counts in docs:
        df.update(counts.keys())

    vectors = []
    for counts in docs:
        vec = {t: (1 + math.log(c)) * math.log(n_docs / (1 + df[t])) for t, c in counts.items()}
        norm = math.sqrt(sum(v * v for v in vec.values())) or 1.0
        vectors.append({t: v / norm for t, v in vec.items()})

    doc = vectors[0]
    out: dict[str, float] = {}
    for name, vec in zip(profiles, vectors[1:]):
        small, large = (doc, vec) if len(doc) <= len(vec) else (vec, doc)
        out[name] = sum(v * large.get(t, 0.0) for t, v in small.items())
    return out


def best_concept(text: str, min_score: float = 0.35) -> tuple[str | None, float]:
    """Nearest concept for unlabelled text. Aliases first, TF-IDF to break ties."""
    if not text.strip():
        return None, 0.0
    lowered = text.lower()
    scores = {name: keyword_score(lowered, terms) for name, terms in CONCEPT_KEYWORDS.items()}
    top = max(scores, key=lambda n: scores[n])
    if scores[top] > 0:
        return top, scores[top]

    tf = tfidf_scores(text, CONCEPT_KEYWORDS)
    name = max(tf, key=lambda n: tf[n])
    return (name, tf[name]) if tf[name] >= min_score else (None, tf[name])


# ----------------------------------------------------------------- corpus IO

def load_plan(delay: float) -> dict[str, list[dict]]:
    """concept -> [{title, text, origin}] from articles.json plus local sources."""
    curated = json.loads(ARTICLES_PATH.read_text(encoding="utf-8"))["concepts"]
    session = requests.Session()
    session.headers.update({"User-Agent": USER_AGENT, "Accept-Language": "en-US,en;q=0.9"})

    plan: dict[str, list[dict]] = {}
    total = len(curated)
    for i, (concept, articles) in enumerate(curated.items(), 1):
        docs: list[dict] = []
        for art in articles:
            got = fetch_article(session, art["url"])
            if got is not None:
                docs.append({"title": got[0], "text": got[1], "origin": art["url"]})
            time.sleep(delay)
        plan[concept] = docs
        words = sum(len(d["text"].split()) for d in docs)
        print(f"[{i:2d}/{total}] {concept:34s} {len(docs)} articles {words:6d} words", flush=True)

    add_local_sources(plan)
    return plan


def add_local_sources(plan: dict[str, list[dict]]) -> None:
    """Merge scripts/corpus/source/*.txt into the plan via keyword + TF-IDF."""
    if not SOURCE_DIR.is_dir():
        return
    files = sorted(p for p in SOURCE_DIR.iterdir() if p.is_file() and p.suffix.lower() == ".txt")
    if not files:
        return
    print(f"\nlocal sources in {SOURCE_DIR}:")
    for path in files:
        text = path.read_text(encoding="utf-8", errors="replace")
        concept, score = best_concept(text)
        if not concept:
            print(f"  ! {path.name}: no concept matched (score {score:.3f}) - skipped")
            continue
        print(f"  {path.name} -> {concept} (score {score:.3f})")
        plan.setdefault(concept, []).append({"title": path.stem, "text": text, "origin": path.name})


def build_files(plan: dict[str, list[dict]], workdir: Path,
                concept_ids: dict[str, str]) -> list[dict]:
    """One .txt per concept, named under SOURCE_PREFIX so --clean can find it."""
    workdir.mkdir(parents=True, exist_ok=True)

    built = []
    for concept, docs in sorted(plan.items()):
        if not docs:
            continue
        parts = [f"{d['title']}\nSource: {d['origin']}\n\n{d['text']}" for d in docs]
        while parts and len("\n\n".join(parts).encode("utf-8")) > MAX_FILE_BYTES:
            parts.pop()  # 20 MiB is the API's cap; trim oldest article first
        body = "\n\n".join(parts)
        path = workdir / f"{SOURCE_PREFIX}{slugify(concept)}.txt"
        path.write_text(body, encoding="utf-8")
        words = len(body.split())
        built.append({
            "concept": concept,
            "conceptId": concept_ids.get(concept),
            "path": path,
            "words": words,
            "chunks": chunk_count(words),
            "articles": len(parts),
        })
    return built


# ------------------------------------------------------------------ postgres

def psql_cmd() -> list[str]:
    """Plain psql when VOXMENTOR_DB is set, else psql inside the compose db container.

    Service and role names follow docker-compose.yml: the service is `db` (it
    used to be `postgres`) and the role/database are both `voxmentor`.
    """
    dsn = os.environ.get("VOXMENTOR_DB")
    if dsn:
        return ["psql", dsn]
    return ["docker", "compose", "exec", "-T", "db", "psql",
            "-U", os.environ.get("VOXMENTOR_PGUSER", "voxmentor"),
            "-d", os.environ.get("VOXMENTOR_PGDATABASE", "voxmentor")]


def psql(sql: str, field_sep: str = "|") -> str:
    """Run SQL, return unaligned rows joined by field_sep."""
    out = subprocess.run(psql_cmd() + ["-t", "-A", "-F", field_sep, "-c", sql],
                         capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(f"psql failed: {out.stderr.strip()[:300]}")
    return out.stdout


def fetch_concept_ids(names: list[str]) -> dict[str, str]:
    """Concept GUIDs keyed by Concepts.Name."""
    quoted = ",".join("'" + n.replace("'", "''") + "'" for n in names)
    rows = psql(f'SELECT "Name", "Id" FROM "Concepts" WHERE "Name" IN ({quoted});')
    ids: dict[str, str] = {}
    for line in rows.splitlines():
        if "|" in line:
            name, cid = line.split("|", 1)
            ids[name.strip()] = cid.strip()
    return ids


# -------------------------------------------------------------------- upload

def login(session: requests.Session, base_url: str, email: str, password: str) -> None:
    resp = session.post(f"{base_url}/api/v1/auth/login",
                        json={"email": email, "password": password}, timeout=30)
    if resp.status_code == 200:
        try:
            if resp.json().get("success"):
                return
        except ValueError:
            pass
    print(f"login failed ({resp.status_code}). Uploads need ContentAdmin or SuperAdmin and\n"
          f"register only grants Student, so grant the role once:\n\n"
          f'  INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")\n'
          f'  SELECT u."Id", r."Id" FROM "AspNetUsers" u, "AspNetRoles" r\n'
          f"  WHERE u.\"Email\" = '{email}' AND r.\"Name\" = 'ContentAdmin';\n")
    sys.exit(1)


def upload(session: requests.Session, base_url: str, item: dict, poll_seconds: int) -> int:
    with item["path"].open("rb") as fh:
        data = {"conceptId": item["conceptId"]} if item["conceptId"] else {}
        resp = session.post(f"{base_url}/api/v1/admin/textbook/upload",
                            files={"file": (item["path"].name, fh, "text/plain")},
                            data=data, timeout=120)
    if resp.status_code != 202:
        raise RuntimeError(f"upload rejected ({resp.status_code}): {resp.text[:300]}")
    job_id = resp.json()["data"]["jobId"]
    print(f"    job {job_id}", flush=True)

    deadline = time.time() + poll_seconds
    status = "Pending"
    while time.time() < deadline:
        time.sleep(3)
        job = session.get(f"{base_url}/api/v1/admin/textbook/status/{job_id}", timeout=30).json()["data"]
        status = job["status"]
        if status in ("Completed", "Failed"):
            if status == "Failed":
                # Ollama or the embedder is unhappy - every later upload would
                # fail the same way, so stop instead of burning 50 more.
                raise RuntimeError(f"ingestion failed: {job.get('error')}")
            return job["totalChunks"]
    raise TimeoutError(f"job {job_id} still {status} after {poll_seconds}s")


# ------------------------------------------------------------------- reports

def report(built: list[dict]) -> int:
    total = sum(b["chunks"] for b in built)
    thin = [b for b in built if b["chunks"] < 5]
    print(f"\n{len(built)} concepts -> {total} chunks "
          f"({sum(b['words'] for b in built)} words, {sum(b['articles'] for b in built)} articles)")
    if thin:
        print("under the 5-chunk bar: "
              + ", ".join("{} ({})".format(b["concept"], b["chunks"]) for b in thin))
    else:
        print("every concept has at least 5 chunks")
    missing = [b["concept"] for b in built if not b["conceptId"]]
    if missing:
        print("no Concepts row (chunks stored without ConceptId): " + ", ".join(missing))
    return total


def embed_query(text: str) -> str:
    base = os.environ.get("OLLAMA_BASE_URL", "http://localhost:11434").rstrip("/")
    model = os.environ.get("EMBED_MODEL", "nomic-embed-text")
    resp = requests.post(f"{base}/api/embed", json={"model": model, "input": text[:8000]}, timeout=60)
    resp.raise_for_status()
    return "[" + ",".join(f"{v:.8f}" for v in resp.json()["embeddings"][0]) + "]"


def verify() -> int:
    total = int(psql(f'SELECT count(*) FROM "TextbookChunks" '
                     f"WHERE \"Source\" LIKE '{SOURCE_PREFIX}%';").strip())
    print(f"seeded chunks: {total} (need >= 500)")

    rows = [line for line in psql(
        'SELECT c."Name", count(k."Id") FROM "Concepts" c '
        'LEFT JOIN "TextbookChunks" k ON k."ConceptId" = c."Id" '
        'GROUP BY c."Name";').splitlines() if "|" in line]
    short = [r for r in rows if int(r.split("|")[1]) < 5]
    print(f"concepts with >= 5 chunks: {len(rows) - len(short)}/{len(rows)}")
    for row in short:
        name, count = row.split("|")
        print(f"  {name:34s} {count}")

    query = "Kadane's algorithm maximum subarray sum"
    vector = embed_query(query)
    hits = psql('SELECT "Source", round(("Embedding" <=> \'' + vector + '\'::vector)::numeric, 4), '
                'left("Content", 100) FROM "TextbookChunks" '
                'ORDER BY "Embedding" <=> \'' + vector + '\'::vector LIMIT 3;')
    print(f"\ncosine search: {query!r}")
    for line in hits.splitlines():
        source, distance, snippet = line.split("|", 2)
        print(f"  {distance:>8}  {source:36s} {snippet}")

    ok = total >= 500 and not short
    print(f"\n{'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


# ----------------------------------------------------------------------- main

def main() -> int:
    ap = argparse.ArgumentParser(description="Seed the DSA textbook corpus (#75)")
    ap.add_argument("mode", choices=["plan", "seed", "verify"])
    ap.add_argument("--base-url", default=os.environ.get("VOXMENTOR_API", "http://localhost:5000"))
    ap.add_argument("--email", default=os.environ.get("VOXMENTOR_ADMIN_EMAIL"))
    ap.add_argument("--password", default=os.environ.get("VOXMENTOR_ADMIN_PASSWORD"))
    ap.add_argument("--clean", action="store_true",
                    help=f"delete chunks whose Source starts with {SOURCE_PREFIX} before seeding")
    ap.add_argument("--delay", type=float, default=1.0, help="seconds between article fetches")
    ap.add_argument("--poll-seconds", type=int, default=600, help="per-job ingestion timeout")
    ap.add_argument("--only", help="handle a single concept (name from seed-dsa-concepts.sql)")
    args = ap.parse_args()

    if args.mode == "verify":
        return verify()

    print(f"loading corpus from {ARTICLES_PATH}")
    plan = load_plan(args.delay)
    if args.only:
        if args.only not in plan:
            sys.exit(f"unknown concept {args.only!r}")
        plan = {args.only: plan[args.only]}

    names = [c for c, docs in plan.items() if docs]
    if not names:
        sys.exit("no article text could be fetched - is the network up?")
    try:
        ids = fetch_concept_ids(names)
    except (RuntimeError, OSError) as exc:
        print("  ! could not read Concepts:")
        print(f"      {exc}")
        print("    chunks will be stored without ConceptId: retrieval still works, "
              "concept-filtered search will not.")
        ids = {}

    with tempfile.TemporaryDirectory(prefix="voxmentor-seed-") as tmp:
        built = build_files(plan, Path(tmp), ids)
        total = report(built)

        if args.mode == "plan":
            print("\nplan only - nothing uploaded")
            return 0 if total >= 500 else 1

        if args.clean:
            print(f"\ncleaning chunks with Source LIKE {SOURCE_PREFIX}%")
            psql(f'''DELETE FROM "TextbookChunks" WHERE "Source" LIKE '{SOURCE_PREFIX}%';''')

        if not args.email or not args.password:
            sys.exit("seed needs --email/--password "
                     "(or VOXMENTOR_ADMIN_EMAIL / VOXMENTOR_ADMIN_PASSWORD)")

        session = requests.Session()
        login(session, args.base_url, args.email, args.password)

        seeded = 0
        for item in built:
            print(f"  {item['concept']:34s} {item['chunks']:3d} chunks", flush=True)
            try:
                stored = upload(session, args.base_url, item, args.poll_seconds)
            except (RuntimeError, TimeoutError) as exc:
                sys.exit(f"\n{exc}\nstopped after {seeded} concept file(s)")
            seeded += 1
            print(f"    stored {stored} chunks", flush=True)

    print(f"\nseeded {seeded} concept file(s); now run: python scripts/seed-corpus.py verify")
    return 0


if __name__ == "__main__":
    sys.exit(main())
