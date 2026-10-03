#!/usr/bin/env python3
"""Offline checks for scripts/seed-corpus.py (issue #75).

No network, no database - just the pure logic the seed leans on: chunk counts
that must match TextChunker.cs, the concept auto-mapper, and the shape of the
curated articles.json.

Run: python -m unittest scripts/test_seed_corpus
"""
import importlib.util
import json
import os
import sys
import tempfile
import unittest
import unittest.mock
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
# the script keeps the issue's hyphenated name, so load it by path
_spec = importlib.util.spec_from_file_location("seed_corpus", SCRIPTS / "seed-corpus.py")
sc = importlib.util.module_from_spec(_spec)
sys.modules["seed_corpus"] = sc
_spec.loader.exec_module(sc)


class ChunkTests(unittest.TestCase):
    def test_chunk_count_matches_chunker_windows(self):
        # mirrors TextChunkerTests: <=375 words is one chunk, step is 338
        self.assertEqual(sc.chunk_count(0), 0)
        self.assertEqual(sc.chunk_count(1), 1)
        self.assertEqual(sc.chunk_count(375), 1)
        self.assertEqual(sc.chunk_count(376), 2)
        self.assertEqual(sc.chunk_count(1000), 3)

    def test_chunk_words_count_agrees_with_chunk_count(self):
        for words in (10, 375, 376, 713, 1000, 2000):
            self.assertEqual(len(sc.chunk_words(" ".join(["w"] * words))), sc.chunk_count(words))

    def test_chunks_cover_all_words_with_overlap(self):
        text = " ".join(str(i) for i in range(1000))
        chunks = sc.chunk_words(text)
        self.assertEqual(chunks[0].split()[0], "0")
        self.assertEqual(chunks[-1].split()[-1], "999")
        self.assertEqual(len(chunks[0].split()), sc.CHUNK_SIZE_WORDS)
        # 37-word overlap between consecutive windows
        self.assertEqual(chunks[1].split()[:sc.CHUNK_OVERLAP_WORDS],
                         chunks[0].split()[-sc.CHUNK_OVERLAP_WORDS:])

    def test_empty_text_yields_nothing(self):
        self.assertEqual(sc.chunk_words(""), [])
        self.assertEqual(sc.chunk_words("   \n "), [])


class MappingTests(unittest.TestCase):
    def test_keyword_match_picks_the_obvious_concept(self):
        text = "A binary search finds a target in a sorted array in O(log n) time."
        concept, score = sc.best_concept(text)
        self.assertEqual(concept, "Binary Search")
        self.assertGreater(score, 0)

    def test_unrelated_text_is_unmapped(self):
        concept, _ = sc.best_concept("banana smoothie recipe with cinnamon and oats")
        self.assertIsNone(concept)

    def test_keyword_score_requires_whole_terms(self):
        # "pop" must not count inside "popular": substring hits mis-map local
        # files to the wrong concept (#111)
        self.assertEqual(sc.keyword_score("popular music", ["pop"]), 0.0)
        self.assertGreater(sc.keyword_score("we push push the button", ["push"]), 0.0)

    def test_every_concept_has_keywords(self):
        # articles.json keys and CONCEPT_KEYWORDS must stay in lockstep or a
        # local source file can never be auto-mapped to its concept
        concepts = json.loads(sc.ARTICLES_PATH.read_text(encoding="utf-8"))["concepts"]
        self.assertEqual(set(concepts), set(sc.CONCEPT_KEYWORDS))


class ArticlesJsonTests(unittest.TestCase):
    def setUp(self):
        self.concepts = json.loads(sc.ARTICLES_PATH.read_text(encoding="utf-8"))["concepts"]

    def test_all_50_concepts_present(self):
        self.assertEqual(len(self.concepts), 50)

    def test_entries_have_url_title_words(self):
        for concept, arts in self.concepts.items():
            self.assertTrue(arts, f"{concept} has no articles")
            for a in arts:
                self.assertTrue(a["url"].startswith("https://www.geeksforgeeks.org/"), a["url"])
                self.assertTrue(a["title"].strip())
                self.assertGreater(a["words"], 0)

    def test_no_duplicate_articles(self):
        seen = set()
        for arts in self.concepts.values():
            for a in arts:
                self.assertNotIn(a["url"], seen, a["url"])
                seen.add(a["url"])


class PgVersionTests(unittest.TestCase):
    def test_iterative_scan_supported_from_0_8(self):
        # the API refuses to boot below 0.8, and hnsw.iterative_scan only exists there
        self.assertTrue(sc._supports_iterative_scan("0.8.0"))
        self.assertTrue(sc._supports_iterative_scan("0.9.1"))
        self.assertTrue(sc._supports_iterative_scan("1.0.0"))
        self.assertFalse(sc._supports_iterative_scan("0.7.4"))
        self.assertFalse(sc._supports_iterative_scan("0.7"))

    def test_missing_or_garbage_version_is_not_supported(self):
        for value in ("", "not-installed", "unknown"):
            self.assertFalse(sc._supports_iterative_scan(value), value)


class BuildFileTests(unittest.TestCase):
    def test_one_file_per_concept_under_the_source_prefix(self):
        plan = {
            "Arrays": [{"title": "Array Basics", "text": " ".join(["word"] * 800),
                        "origin": "https://example.test/a"}],
            "Stacks": [{"title": "Stack Basics", "text": " ".join(["word"] * 500),
                        "origin": "https://example.test/s"}],
        }
        ids = {"Arrays": "11111111-1111-1111-1111-111111111111"}
        with tempfile.TemporaryDirectory() as tmp:
            built = sc.build_files(plan, Path(tmp), ids)
            self.assertEqual([b["concept"] for b in built], ["Arrays", "Stacks"])
            for b in built:
                self.assertTrue(b["path"].exists())
                # the file name keeps the seed prefix so --clean/verify find it
                self.assertTrue(b["path"].name.startswith(sc.SOURCE_PREFIX))
                self.assertEqual(b["conceptId"], ids.get(b["concept"]))
            self.assertEqual(built[0]["chunks"], sc.chunk_count(800))
            self.assertEqual(built[0]["path"].read_text(encoding="utf-8").count("Source: "), 1)

    def test_concept_without_a_db_row_still_builds(self):
        plan = {"Stacks": [{"title": "S", "text": "word " * 400, "origin": "x"}]}
        with tempfile.TemporaryDirectory() as tmp:
            built = sc.build_files(plan, Path(tmp), {})
            self.assertEqual(len(built), 1)
            self.assertIsNone(built[0]["conceptId"])

    def test_oversized_concept_trims_oldest_article(self):
        plan = {"Arrays": [
            {"title": "A", "text": "word " * 400, "origin": "a"},
            {"title": "B", "text": "word " * 400, "origin": "b"},
        ]}
        original = sc.MAX_FILE_BYTES
        sc.MAX_FILE_BYTES = 4000  # only one article fits
        try:
            with tempfile.TemporaryDirectory() as tmp:
                built = sc.build_files(plan, Path(tmp), {})
                self.assertEqual(built[0]["articles"], 1)
                self.assertLessEqual(built[0]["path"].stat().st_size, 4000)
        finally:
            sc.MAX_FILE_BYTES = original

    def test_concept_with_no_fitting_article_is_skipped(self):
        # a single article over the cap trims itself to nothing; the concept
        # must be skipped, not written as an empty file (#111)
        plan = {"Arrays": [{"title": "A", "text": "word " * 400, "origin": "a"}]}
        original = sc.MAX_FILE_BYTES
        sc.MAX_FILE_BYTES = 100
        try:
            with tempfile.TemporaryDirectory() as tmp:
                built = sc.build_files(plan, Path(tmp), {})
                self.assertEqual(built, [])
                self.assertEqual(list(Path(tmp).glob("*.txt")), [])
        finally:
            sc.MAX_FILE_BYTES = original


class PlanExitTests(unittest.TestCase):
    def test_full_plan_needs_five_per_concept_and_500_total(self):
        self.assertTrue(sc.plan_ok([{"chunks": 5}] * 100, None))   # exactly 500
        self.assertFalse(sc.plan_ok([{"chunks": 10}] * 49, None))  # 490 total
        self.assertFalse(sc.plan_ok([{"chunks": 4}] + [{"chunks": 100}] * 10, None))

    def test_only_plan_skips_the_500_bar_but_keeps_five(self):
        # one healthy concept (~11 chunks) must pass; CodeRabbit #111
        self.assertTrue(sc.plan_ok([{"chunks": 11}], "Binary Search Trees"))
        self.assertFalse(sc.plan_ok([{"chunks": 3}], "Binary Search Trees"))
        self.assertFalse(sc.plan_ok([], "Binary Search Trees"))


class RelevanceTests(unittest.TestCase):
    def test_seeded_arrays_or_kadane_hit_counts(self):
        self.assertTrue(sc.relevant_hit(f"{sc.SOURCE_PREFIX}arrays.txt"))
        self.assertTrue(sc.relevant_hit(f"{sc.SOURCE_PREFIX}kadanes-algorithm.txt"))

    def test_unseeded_or_wrong_concept_does_not_count(self):
        self.assertFalse(sc.relevant_hit("manual-arrays.txt"))  # missing prefix
        self.assertFalse(sc.relevant_hit(f"{sc.SOURCE_PREFIX}stacks.txt"))
        self.assertFalse(sc.relevant_hit(f"{sc.SOURCE_PREFIX}trees.txt"))


class CleanOrderTests(unittest.TestCase):
    """--clean must not delete anything until every upload succeeds (#111).

    These run main() end to end with the network, database and run lock
    mocked out, so a revert to the baseline clean-before-upload ordering
    fails here instead of shipping a data-loss regression.
    """

    def _run_seed(self, upload_effect):
        plan = {"Arrays": [{"title": "A", "text": "word " * 400, "origin": "a"}]}
        events = []

        def upload(*args, **kwargs):
            events.append("upload")
            if isinstance(upload_effect, BaseException):
                raise upload_effect
            return upload_effect

        def psql(sql, field_sep="|"):
            events.append(("psql", sql))
            return ""

        with unittest.mock.patch.object(sc, "load_plan", return_value=plan), \
                unittest.mock.patch.object(sc, "fetch_concept_ids", return_value={}), \
                unittest.mock.patch.object(sc, "login"), \
                unittest.mock.patch.object(sc, "acquire_run_lock") as lock_mock, \
                unittest.mock.patch.object(sc, "upload", side_effect=upload), \
                unittest.mock.patch.object(sc, "psql", side_effect=psql), \
                unittest.mock.patch.object(
                    sys, "argv",
                    ["seed-corpus.py", "seed", "--clean",
                     "--email", "a@b.c", "--password", "x"]):
            try:
                result = sc.main()
            except SystemExit as exc:
                result = exc
        return result, events, lock_mock

    def test_clean_runs_after_every_upload_and_excludes_own_job(self):
        result, events, lock_mock = self._run_seed(("job-1", 11))
        lock_mock.assert_called_once()  # seed mode must take the run lock (#111)
        self.assertEqual(result, 0)
        self.assertEqual(len(events), 2)
        self.assertEqual(events[0], "upload")
        self.assertTrue(events[1][1].lstrip().startswith("DELETE"))
        self.assertIn('AND "JobId" NOT IN (\'job-1\')', events[1][1])
        self.assertIn('AND "Source" IN (\'seed-corpus-arrays.txt\')', events[1][1])

    def test_failed_upload_leaves_the_previous_corpus_alone(self):
        result, events, lock_mock = self._run_seed(RuntimeError("ingestion failed: test"))
        lock_mock.assert_called_once()
        self.assertIsInstance(result, SystemExit)
        self.assertEqual(events, ["upload"])  # upload failed, no DELETE ran

    def test_plan_mode_never_takes_the_run_lock(self):
        # 1500 words = 5 chunks, the --only floor in plan_ok (#111)
        plan = {"Arrays": [{"title": "A", "text": "word " * 1500, "origin": "a"}]}
        with unittest.mock.patch.object(sc, "load_plan", return_value=plan), \
                unittest.mock.patch.object(sc, "fetch_concept_ids", return_value={}), \
                unittest.mock.patch.object(sc, "acquire_run_lock") as lock_mock, \
                unittest.mock.patch.object(
                    sys, "argv",
                    ["seed-corpus.py", "plan", "--only", "Arrays"]):
            result = sc.main()
        self.assertEqual(result, 0)
        lock_mock.assert_not_called()


class RunLockTests(unittest.TestCase):
    """acquire_run_lock must hold a real OS lock for the whole run (#111).

    The lock is the only thing standing between two overlapping
    seed --clean runs and mutual deletion, so it gets real coverage:
    a held lock refuses a second acquire, and a lock-primitive failure
    exits before anything else runs.
    """

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        # isolated lock location: never contends with a real seed run
        patcher = unittest.mock.patch.object(
            sc.tempfile, "gettempdir", return_value=tmp.name)
        patcher.start()
        self.addCleanup(patcher.stop)

        def release():
            if sc._RUN_LOCK is not None:
                sc._RUN_LOCK.close()
                sc._RUN_LOCK = None

        self.addCleanup(release)  # runs first (LIFO): unlock before tmp cleanup

    def test_second_acquire_is_refused_while_the_first_hold_is_live(self):
        sc.acquire_run_lock()
        self.assertIsNotNone(sc._RUN_LOCK)
        self.assertFalse(sc._RUN_LOCK.closed)
        with self.assertRaises(SystemExit) as ctx:
            sc.acquire_run_lock()
        self.assertIn("another seed run already holds", str(ctx.exception))

    def test_lock_primitive_failure_exits_with_the_refusal_message(self):
        # every OSError from the lock call means "refuse", whatever the cause
        target = "msvcrt.locking" if os.name == "nt" else "fcntl.flock"
        with unittest.mock.patch(target, side_effect=OSError(33, "busy")):
            with self.assertRaises(SystemExit) as ctx:
                sc.acquire_run_lock()
        self.assertIn("another seed run already holds", str(ctx.exception))
        self.assertIsNone(sc._RUN_LOCK)  # never held, so never retained


if __name__ == "__main__":
    unittest.main()
