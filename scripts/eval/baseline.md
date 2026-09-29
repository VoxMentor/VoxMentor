# Tutor eval baseline (#77)

Reference baseline for `scripts/eval-tutor.py run` — first green full run.

## Environment

- Date: 2026-09-28
- Box: Windows dev laptop, CPU-only (llama3.2:3b ≈ 1 tok/s)
- Ollama models: `nomic-embed-text` (embed), `llama3.2:3b` (chat)
- DB: dev Supabase; 17 eval chunks from `scripts/eval/corpus.txt` via `python scripts/eval-tutor.py seed`
- Command: `python -u scripts/eval-tutor.py run --max-avg-latency-s 150`

## Results (20 golden pairs, k sweep 3/5/7, winner k=3)

| Metric | Value | Gate |
| --- | --- | --- |
| recall @ k=3 (winner) | 1.00 | ≥ 0.80 pass |
| MRR @ k=3 | 0.917 | reported |
| recall @ k=5 / k=7 | 1.00 / 1.00 | — |
| citation precision (mean) | 1.00 | ≥ 0.80 pass |
| keyphrase coverage (mean) | 0.867 | reported only |
| expected-source cited rate | 0.85 | reported only |
| generation latency mean / p95 | 127.8 s / 201.5 s | ≤ 150 s (override)* |

* `max_avg_latency_s` in `golden-qa.json` is 8.0 s (production-HW target). This CPU-only
box cannot approach it (≈1 tok/s), so this run used `--max-avg-latency-s 150`. Re-run
without the override on production hardware to enforce the defined gate.

## Notes

- Winner k=3: recall and MRR tie across k=3/5/7, so the smallest k wins (mean cosine
  distance agrees: 0.326 < 0.341 < 0.351). Prod `Rag:TopK=5` also scores recall 1.00.
- Citation precision improved to 1.00 after prompt hardening (delimiter tags + escape,
  CodeRabbit #96). Previously gqa-07 failed (0.0); all 20 now cite correctly.
- Retrieval bug found by this eval and fixed before this baseline was taken: the IVFFlat
  embedding index on the small `TextbookChunks` table returned 0–1 rows under `LIMIT`
  plans (recall was 0.65 pre-fix); migration `SwitchTextbookEmbeddingIndexToHnsw`.
- Per-run detail (answers, per-golden ranks) lands in `scripts/eval/report.json` (gitignored).
