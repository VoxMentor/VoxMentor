-- scripts/seed-bkt-parameters.sql
-- Seeds BKT parameters for all DSA concepts with default values.
-- Idempotent: safe to run multiple times.

INSERT INTO "BktParameters" ("Id", "ConceptId", "PriorKnowledge", "LearnRate", "GuessRate", "SlipRate", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), "Id", 0.1, 0.3, 0.2, 0.1, now(), now()
FROM "Concepts"
ON CONFLICT ("ConceptId") DO NOTHING;
