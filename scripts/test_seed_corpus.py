#!/usr/bin/env python3
"""Offline checks for scripts/seed-corpus.py (issue #75).

No network, no database - just the pure logic the seed leans on: chunk counts
that must match TextChunker.cs, the concept auto-mapper, and the shape of the
curated articles.json.

Run: python -m unittest scripts/test_seed_corpus
"""
import importlib.util
import json
import sys
import tempfile
import unittest
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


if __name__ == "__main__":
    unittest.main()
