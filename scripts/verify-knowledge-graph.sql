-- scripts/verify-knowledge-graph.sql
-- Post-seed verification for the DSA prerequisite knowledge graph (issue #9).
-- Run AFTER seed-dsa-concepts.sql. Read-only; safe to run any number of times.
--
-- Sections:
--   1. Row counts          -> concepts = 50, edges = 71
--   2. Graph integrity     -> 0 orphans, 0 self-edges, 0 cycles
--   3. Recursive CTE: prerequisite chain (transitive closure upward)
--   4. Recursive CTE: unlocked children (forward traversal)
--   5. "Almost eligible" analysis (exactly one unmet prerequisite)

-- ============================================================
-- 1. Row counts
-- ============================================================
SELECT COUNT(*) AS concept_count FROM "Concepts";        -- expect 50
SELECT COUNT(*) AS edge_count   FROM "Prerequisites";    -- expect 71

-- ============================================================
-- 2. Graph integrity
-- ============================================================
-- Every edge endpoint must exist in Concepts (orphans = 0)
SELECT COUNT(*) AS orphan_edges
FROM "Prerequisites" p
WHERE NOT EXISTS (SELECT 1 FROM "Concepts" c WHERE c."Id" = p."ConceptId")
   OR NOT EXISTS (SELECT 1 FROM "Concepts" c WHERE c."Id" = p."RequiredConceptId");

-- No concept may require itself (self-edges = 0)
SELECT COUNT(*) AS self_edges
FROM "Prerequisites"
WHERE "ConceptId" = "RequiredConceptId";

-- Transitive closure via UNION-recursive CTE (dedupes, so it always
-- terminates). Any start_id = reached_id row means a cycle (expect 0).
WITH RECURSIVE reach AS (
    SELECT DISTINCT p."ConceptId" AS start_id, p."RequiredConceptId" AS reached_id
    FROM "Prerequisites" p
    UNION
    SELECT r.start_id, p."RequiredConceptId"
    FROM reach r
    JOIN "Prerequisites" p ON p."ConceptId" = r.reached_id
)
SELECT COUNT(*) AS cyclic_pairs FROM reach WHERE start_id = reached_id;

-- ============================================================
-- 3. Recursive CTE: full prerequisite chain for one concept
--    Walks upward: ConceptId -> RequiredConceptId until a root.
--    Default: Dijkstra (concept 37). Swap the UUID to inspect others.
-- ============================================================
WITH RECURSIVE prereq_chain AS (
    SELECT p."ConceptId", p."RequiredConceptId", 1 AS depth
    FROM "Prerequisites" p
    WHERE p."ConceptId" = 'a0000001-0000-0000-0000-000000000037'
    UNION ALL
    SELECT p."ConceptId", p."RequiredConceptId", pc.depth + 1
    FROM "Prerequisites" p
    JOIN prereq_chain pc ON p."ConceptId" = pc."RequiredConceptId"
)
SELECT DISTINCT pc.depth, c."Name"
FROM prereq_chain pc
JOIN "Concepts" c ON c."Id" = pc."RequiredConceptId"
ORDER BY pc.depth DESC, c."Name";

-- ============================================================
-- 4. Recursive CTE: what does mastering a concept unlock?
--    Forward traversal: RequiredConceptId -> ConceptId (children).
--    Default: Variables (concept 1), the graph's root.
-- ============================================================
WITH RECURSIVE unlocked AS (
    SELECT p."ConceptId", p."RequiredConceptId", 1 AS depth
    FROM "Prerequisites" p
    WHERE p."RequiredConceptId" = 'a0000001-0000-0000-0000-000000000001'
    UNION ALL
    SELECT p."ConceptId", p."RequiredConceptId", u.depth + 1
    FROM "Prerequisites" p
    JOIN unlocked u ON p."RequiredConceptId" = u."ConceptId"
)
SELECT DISTINCT u.depth, c."Name"
FROM unlocked u
JOIN "Concepts" c ON c."Id" = u."ConceptId"
ORDER BY u.depth, c."Name";

-- ============================================================
-- 5. "Almost eligible" analysis
--    Concepts the student could unlock next: not yet mastered and
--    exactly ONE prerequisite still missing. For eligible-now
--    recommendations use missing = 0.
--    Replace <user-id-here> with the ApplicationUser Id.
-- ============================================================
WITH mastered AS (
    SELECT "ConceptId"
    FROM "StudentMasteries"
    WHERE "UserId" = '<user-id-here>'
),
unmet AS (
    SELECT p."ConceptId",
           COUNT(*) FILTER (WHERE m."ConceptId" IS NULL) AS missing
    FROM "Prerequisites" p
    LEFT JOIN mastered m ON m."ConceptId" = p."RequiredConceptId"
    GROUP BY p."ConceptId"
)
SELECT c."Name", u.missing AS unmet_prerequisite_count
FROM unmet u
JOIN "Concepts" c ON c."Id" = u."ConceptId"
LEFT JOIN mastered m ON m."ConceptId" = c."Id"
WHERE u.missing = 1
  AND m."ConceptId" IS NULL
ORDER BY c."Name";
