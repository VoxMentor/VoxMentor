-- scripts/seed-dsa-concepts.sql
-- Seeds 50 DSA concepts and the prerequisite knowledge graph (issue #9).
-- Idempotent: safe to run multiple times (ON CONFLICT DO NOTHING).
--
-- Run order: this file FIRST, then seed-bkt-parameters.sql
-- (which seeds BKT parameters for every row in Concepts).
--
-- Concept layout (difficulty 1-4, categories 1-6):
--   1-5   Fundamentals        a0000001-...-0001 .. a0000001-...-0005
--   6-13  Data Structures     ...0006 .. ...0013
--   14    Algorithms (Recursion) ...0014
--   15    Searching           ...0015
--   16-18 Techniques          ...0016 .. ...0018
--   19-24 Sorting             ...0019 .. ...0024
--   25-30 Trees               ...0025 .. ...0030
--   31-44 Graphs              ...0031 .. ...0044
--   45    Algorithms (Backtracking) ...0045
--   46-49 Dynamic Programming ...0046 .. ...0049
--   50    Algorithms (Greedy) ...0050
--
-- Prerequisite semantics: (ConceptId, RequiredConceptId) means
-- "ConceptId requires RequiredConceptId first".

BEGIN;

INSERT INTO "Concepts" ("Id", "Name", "Description", "DifficultyLevel", "Category", "CreatedAt") VALUES
-- Fundamentals (1-5)
('a0000001-0000-0000-0000-000000000001', 'Variables and Data Types', 'Primitives, references, and how values are stored and typed in memory.', 1, 'Fundamentals', now()),
('a0000001-0000-0000-0000-000000000002', 'Control Flow', 'Conditionals and loops: if/else, for, while, and iteration patterns.', 1, 'Fundamentals', now()),
('a0000001-0000-0000-0000-000000000003', 'Functions', 'Parameters, return values, call stack basics, and decomposition.', 1, 'Fundamentals', now()),
('a0000001-0000-0000-0000-000000000004', 'Time Complexity', 'Big-O analysis: O(1), O(log n), O(n), O(n^2) and comparing algorithms.', 1, 'Fundamentals', now()),
('a0000001-0000-0000-0000-000000000005', 'Space Complexity', 'Measuring auxiliary memory usage, including recursion stack depth.', 1, 'Fundamentals', now()),
-- Data Structures (6-13)
('a0000001-0000-0000-0000-000000000006', 'Arrays', 'Contiguous indexed storage; traversal, insertion, deletion trade-offs.', 2, 'Data Structures', now()),
('a0000001-0000-0000-0000-000000000007', 'Strings', 'Immutability, slicing, building, and common string manipulation.', 2, 'Data Structures', now()),
('a0000001-0000-0000-0000-000000000008', 'Hash Maps and Sets', 'O(1) average lookup: frequency counting, dedup, two-sum style lookups.', 2, 'Data Structures', now()),
('a0000001-0000-0000-0000-000000000009', 'Singly Linked Lists', 'Node-based sequential storage; reversal, merging, cycle basics.', 2, 'Data Structures', now()),
('a0000001-0000-0000-0000-000000000010', 'Doubly Linked Lists', 'Bidirectional node links; insertion and removal at both ends.', 2, 'Data Structures', now()),
('a0000001-0000-0000-0000-000000000011', 'Stacks', 'LIFO mechanics; matching brackets, undo, and call-stack model.', 2, 'Data Structures', now()),
('a0000001-0000-0000-0000-000000000012', 'Queues', 'FIFO mechanics; scheduling and level-order processing.', 2, 'Data Structures', now()),
('a0000001-0000-0000-0000-000000000013', 'Deque', 'Double-ended queue; push/pop at both ends, sliding-window maximum.', 2, 'Data Structures', now()),
-- Algorithms: Recursion (14)
('a0000001-0000-0000-0000-000000000014', 'Recursion', 'Base case vs recursive case, call stack visualization, multiple calls.', 2, 'Algorithms', now()),
-- Searching (15)
('a0000001-0000-0000-0000-000000000015', 'Binary Search', 'O(log n) search on sorted data; boundary and modified variants.', 2, 'Searching', now()),
-- Techniques (16-18)
('a0000001-0000-0000-0000-000000000016', 'Two Pointers', 'Converging or same-direction pointers on arrays and strings.', 2, 'Techniques', now()),
('a0000001-0000-0000-0000-000000000017', 'Sliding Window', 'Fixed and variable windows for subarray and substring problems.', 2, 'Techniques', now()),
('a0000001-0000-0000-0000-000000000018', 'Prefix Sum', 'Precomputed running totals for O(1) range sum queries.', 2, 'Techniques', now()),
-- Sorting (19-24)
('a0000001-0000-0000-0000-000000000019', 'Bubble Sort', 'Adjacent-swap comparison sort; O(n^2) baseline.', 2, 'Sorting', now()),
('a0000001-0000-0000-0000-000000000020', 'Selection Sort', 'Select-min comparison sort; O(n^2), in-place.', 2, 'Sorting', now()),
('a0000001-0000-0000-0000-000000000021', 'Insertion Sort', 'Build sorted prefix incrementally; O(n^2), adaptive.', 2, 'Sorting', now()),
('a0000001-0000-0000-0000-000000000022', 'Merge Sort', 'Divide-and-conquer O(n log n) sort; stable, needs O(n) space.', 3, 'Sorting', now()),
('a0000001-0000-0000-0000-000000000023', 'Quick Sort', 'Partition-based divide-and-conquer sort; average O(n log n).', 3, 'Sorting', now()),
('a0000001-0000-0000-0000-000000000024', 'Counting Sort', 'Non-comparison sort for bounded integer keys; O(n + k).', 2, 'Sorting', now()),
-- Trees (25-30)
('a0000001-0000-0000-0000-000000000025', 'Binary Trees', 'Hierarchical nodes; height, depth, diameter properties.', 3, 'Trees', now()),
('a0000001-0000-0000-0000-000000000026', 'Binary Search Trees', 'Left < root < right ordering; search, insert, delete.', 3, 'Trees', now()),
('a0000001-0000-0000-0000-000000000027', 'Tree Traversals', 'Preorder, inorder, postorder, and level-order traversal.', 3, 'Trees', now()),
('a0000001-0000-0000-0000-000000000028', 'Heap Data Structure', 'Complete binary tree invariant; array representation, heapify.', 3, 'Trees', now()),
('a0000001-0000-0000-0000-000000000029', 'Priority Queues', 'Heap-backed queue; top-k extraction and scheduling.', 3, 'Trees', now()),
('a0000001-0000-0000-0000-000000000030', 'Trie', 'Prefix tree for strings; autocomplete and word lookup.', 3, 'Trees', now()),
-- Graphs (31-44)
('a0000001-0000-0000-0000-000000000031', 'Graph Representations', 'Adjacency list vs adjacency matrix; directed, undirected, weighted.', 3, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000032', 'Breadth-First Search (BFS)', 'Level-by-level exploration; shortest path in unweighted graphs.', 3, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000033', 'Depth-First Search (DFS)', 'Deep exploration with a stack; connectivity and backtracking.', 3, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000034', 'Topological Sort', 'Dependency ordering of DAG nodes via DFS or Kahn''s algorithm.', 3, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000035', 'Cycle Detection', 'Detect cycles with visited/recursion-stack coloring or union-find.', 3, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000036', 'Union-Find', 'Disjoint set union with path compression and union by rank.', 3, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000037', 'Dijkstra''s Algorithm', 'Single-source shortest path with non-negative weights via a priority queue.', 4, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000038', 'Bellman-Ford Algorithm', 'Shortest path tolerating negative weights; negative cycle detection.', 4, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000039', 'Floyd-Warshall Algorithm', 'All-pairs shortest paths via dynamic programming on a matrix.', 4, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000040', 'Kruskal''s Algorithm', 'Minimum spanning tree by sorting edges and union-find merging.', 4, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000041', 'Prim''s Algorithm', 'Minimum spanning tree by growing the tree from a start vertex.', 4, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000042', 'Weighted Graphs', 'Edge weights and their effect on traversal and shortest paths.', 3, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000043', 'Shortest Path Algorithms', 'Choosing the right shortest-path algorithm for the graph type.', 4, 'Graphs', now()),
('a0000001-0000-0000-0000-000000000044', 'Minimum Spanning Tree', 'Connecting all vertices with minimum total edge weight.', 4, 'Graphs', now()),
-- Algorithms: Backtracking (45)
('a0000001-0000-0000-0000-000000000045', 'Backtracking', 'Choose, explore, unchoose: permutations, subsets, N-Queens, pruning.', 3, 'Algorithms', now()),
-- Dynamic Programming (46-49)
('a0000001-0000-0000-0000-000000000046', 'Memoization', 'Top-down DP: cache recursive results in a hash map or array.', 3, 'Dynamic Programming', now()),
('a0000001-0000-0000-0000-000000000047', 'Tabulation', 'Bottom-up DP: fill a table from base cases iteratively.', 3, 'Dynamic Programming', now()),
('a0000001-0000-0000-0000-000000000048', 'Knapsack Problem', '0/1 and unbounded knapsack: capacity states and take/skip decisions.', 4, 'Dynamic Programming', now()),
('a0000001-0000-0000-0000-000000000049', 'Longest Common Subsequence', 'Classic 2D string DP; edit distance as a related problem.', 4, 'Dynamic Programming', now()),
-- Algorithms: Greedy (50)
('a0000001-0000-0000-0000-000000000050', 'Greedy Algorithms', 'Local optimal choices with proofs of global optimality.', 3, 'Algorithms', now());

INSERT INTO "Prerequisites" ("Id", "ConceptId", "RequiredConceptId", "Weight", "CreatedAt")
SELECT gen_random_uuid(), v."ConceptId"::uuid, v."RequiredConceptId"::uuid, 1, now()
FROM (VALUES
    -- Fundamentals chain
    ('a0000001-0000-0000-0000-000000000003', 'a0000001-0000-0000-0000-000000000001'), -- Functions <- Variables
    ('a0000001-0000-0000-0000-000000000004', 'a0000001-0000-0000-0000-000000000002'), -- Time Complexity <- Control Flow
    ('a0000001-0000-0000-0000-000000000004', 'a0000001-0000-0000-0000-000000000003'), -- Time Complexity <- Functions
    ('a0000001-0000-0000-0000-000000000005', 'a0000001-0000-0000-0000-000000000004'), -- Space Complexity <- Time Complexity
    -- Arrays and its dependents
    ('a0000001-0000-0000-0000-000000000006', 'a0000001-0000-0000-0000-000000000001'), -- Arrays <- Variables
    ('a0000001-0000-0000-0000-000000000007', 'a0000001-0000-0000-0000-000000000006'), -- Strings <- Arrays
    ('a0000001-0000-0000-0000-000000000008', 'a0000001-0000-0000-0000-000000000006'), -- Hash Maps <- Arrays
    ('a0000001-0000-0000-0000-000000000009', 'a0000001-0000-0000-0000-000000000006'), -- Singly Linked Lists <- Arrays
    ('a0000001-0000-0000-0000-000000000010', 'a0000001-0000-0000-0000-000000000009'), -- Doubly Linked Lists <- Singly
    ('a0000001-0000-0000-0000-000000000011', 'a0000001-0000-0000-0000-000000000006'), -- Stacks <- Arrays
    ('a0000001-0000-0000-0000-000000000012', 'a0000001-0000-0000-0000-000000000009'), -- Queues <- Singly Linked Lists
    ('a0000001-0000-0000-0000-000000000013', 'a0000001-0000-0000-0000-000000000010'), -- Deque <- Doubly Linked Lists
    ('a0000001-0000-0000-0000-000000000013', 'a0000001-0000-0000-0000-000000000011'), -- Deque <- Stacks
    -- Recursion is the pivot
    ('a0000001-0000-0000-0000-000000000014', 'a0000001-0000-0000-0000-000000000003'), -- Recursion <- Functions
    -- Searching / Techniques
    ('a0000001-0000-0000-0000-000000000015', 'a0000001-0000-0000-0000-000000000006'), -- Binary Search <- Arrays
    ('a0000001-0000-0000-0000-000000000015', 'a0000001-0000-0000-0000-000000000004'), -- Binary Search <- Time Complexity
    ('a0000001-0000-0000-0000-000000000016', 'a0000001-0000-0000-0000-000000000006'), -- Two Pointers <- Arrays
    ('a0000001-0000-0000-0000-000000000017', 'a0000001-0000-0000-0000-000000000006'), -- Sliding Window <- Arrays
    ('a0000001-0000-0000-0000-000000000017', 'a0000001-0000-0000-0000-000000000008'), -- Sliding Window <- Hash Maps
    ('a0000001-0000-0000-0000-000000000018', 'a0000001-0000-0000-0000-000000000006'), -- Prefix Sum <- Arrays
    -- Sorting
    ('a0000001-0000-0000-0000-000000000019', 'a0000001-0000-0000-0000-000000000006'), -- Bubble Sort <- Arrays
    ('a0000001-0000-0000-0000-000000000019', 'a0000001-0000-0000-0000-000000000004'), -- Bubble Sort <- Time Complexity
    ('a0000001-0000-0000-0000-000000000020', 'a0000001-0000-0000-0000-000000000006'), -- Selection Sort <- Arrays
    ('a0000001-0000-0000-0000-000000000020', 'a0000001-0000-0000-0000-000000000004'), -- Selection Sort <- Time Complexity
    ('a0000001-0000-0000-0000-000000000021', 'a0000001-0000-0000-0000-000000000006'), -- Insertion Sort <- Arrays
    ('a0000001-0000-0000-0000-000000000021', 'a0000001-0000-0000-0000-000000000004'), -- Insertion Sort <- Time Complexity
    ('a0000001-0000-0000-0000-000000000022', 'a0000001-0000-0000-0000-000000000014'), -- Merge Sort <- Recursion
    ('a0000001-0000-0000-0000-000000000022', 'a0000001-0000-0000-0000-000000000006'), -- Merge Sort <- Arrays
    ('a0000001-0000-0000-0000-000000000023', 'a0000001-0000-0000-0000-000000000014'), -- Quick Sort <- Recursion
    ('a0000001-0000-0000-0000-000000000023', 'a0000001-0000-0000-0000-000000000006'), -- Quick Sort <- Arrays
    ('a0000001-0000-0000-0000-000000000024', 'a0000001-0000-0000-0000-000000000008'), -- Counting Sort <- Hash Maps
    -- Trees
    ('a0000001-0000-0000-0000-000000000025', 'a0000001-0000-0000-0000-000000000014'), -- Binary Trees <- Recursion
    ('a0000001-0000-0000-0000-000000000026', 'a0000001-0000-0000-0000-000000000025'), -- BST <- Binary Trees
    ('a0000001-0000-0000-0000-000000000026', 'a0000001-0000-0000-0000-000000000015'), -- BST <- Binary Search
    ('a0000001-0000-0000-0000-000000000027', 'a0000001-0000-0000-0000-000000000025'), -- Traversals <- Binary Trees
    ('a0000001-0000-0000-0000-000000000027', 'a0000001-0000-0000-0000-000000000011'), -- Traversals <- Stacks
    ('a0000001-0000-0000-0000-000000000028', 'a0000001-0000-0000-0000-000000000025'), -- Heap <- Binary Trees
    ('a0000001-0000-0000-0000-000000000028', 'a0000001-0000-0000-0000-000000000006'), -- Heap <- Arrays
    ('a0000001-0000-0000-0000-000000000029', 'a0000001-0000-0000-0000-000000000028'), -- Priority Queues <- Heap
    ('a0000001-0000-0000-0000-000000000030', 'a0000001-0000-0000-0000-000000000025'), -- Trie <- Binary Trees
    ('a0000001-0000-0000-0000-000000000030', 'a0000001-0000-0000-0000-000000000007'), -- Trie <- Strings
    -- Graphs
    ('a0000001-0000-0000-0000-000000000031', 'a0000001-0000-0000-0000-000000000006'), -- Graph Representations <- Arrays
    ('a0000001-0000-0000-0000-000000000031', 'a0000001-0000-0000-0000-000000000008'), -- Graph Representations <- Hash Maps
    ('a0000001-0000-0000-0000-000000000032', 'a0000001-0000-0000-0000-000000000012'), -- BFS <- Queues
    ('a0000001-0000-0000-0000-000000000032', 'a0000001-0000-0000-0000-000000000031'), -- BFS <- Graph Representations
    ('a0000001-0000-0000-0000-000000000033', 'a0000001-0000-0000-0000-000000000011'), -- DFS <- Stacks
    ('a0000001-0000-0000-0000-000000000033', 'a0000001-0000-0000-0000-000000000031'), -- DFS <- Graph Representations
    ('a0000001-0000-0000-0000-000000000034', 'a0000001-0000-0000-0000-000000000033'), -- Topological Sort <- DFS
    ('a0000001-0000-0000-0000-000000000035', 'a0000001-0000-0000-0000-000000000033'), -- Cycle Detection <- DFS
    ('a0000001-0000-0000-0000-000000000035', 'a0000001-0000-0000-0000-000000000032'), -- Cycle Detection <- BFS
    ('a0000001-0000-0000-0000-000000000036', 'a0000001-0000-0000-0000-000000000031'), -- Union-Find <- Graph Representations
    ('a0000001-0000-0000-0000-000000000037', 'a0000001-0000-0000-0000-000000000032'), -- Dijkstra <- BFS
    ('a0000001-0000-0000-0000-000000000037', 'a0000001-0000-0000-0000-000000000029'), -- Dijkstra <- Priority Queues
    ('a0000001-0000-0000-0000-000000000038', 'a0000001-0000-0000-0000-000000000031'), -- Bellman-Ford <- Graph Representations
    ('a0000001-0000-0000-0000-000000000039', 'a0000001-0000-0000-0000-000000000031'), -- Floyd-Warshall <- Graph Representations
    ('a0000001-0000-0000-0000-000000000040', 'a0000001-0000-0000-0000-000000000036'), -- Kruskal <- Union-Find
    ('a0000001-0000-0000-0000-000000000041', 'a0000001-0000-0000-0000-000000000029'), -- Prim <- Priority Queues
    ('a0000001-0000-0000-0000-000000000042', 'a0000001-0000-0000-0000-000000000031'), -- Weighted Graphs <- Graph Representations
    ('a0000001-0000-0000-0000-000000000043', 'a0000001-0000-0000-0000-000000000037'), -- Shortest Path <- Dijkstra
    ('a0000001-0000-0000-0000-000000000043', 'a0000001-0000-0000-0000-000000000038'), -- Shortest Path <- Bellman-Ford
    ('a0000001-0000-0000-0000-000000000044', 'a0000001-0000-0000-0000-000000000040'), -- MST <- Kruskal
    ('a0000001-0000-0000-0000-000000000044', 'a0000001-0000-0000-0000-000000000041'), -- MST <- Prim
    -- Backtracking / DP
    ('a0000001-0000-0000-0000-000000000045', 'a0000001-0000-0000-0000-000000000014'), -- Backtracking <- Recursion
    ('a0000001-0000-0000-0000-000000000046', 'a0000001-0000-0000-0000-000000000014'), -- Memoization <- Recursion
    ('a0000001-0000-0000-0000-000000000046', 'a0000001-0000-0000-0000-000000000008'), -- Memoization <- Hash Maps
    ('a0000001-0000-0000-0000-000000000047', 'a0000001-0000-0000-0000-000000000046'), -- Tabulation <- Memoization
    ('a0000001-0000-0000-0000-000000000048', 'a0000001-0000-0000-0000-000000000047'), -- Knapsack <- Tabulation
    ('a0000001-0000-0000-0000-000000000049', 'a0000001-0000-0000-0000-000000000047'), -- LCS <- Tabulation
    ('a0000001-0000-0000-0000-000000000049', 'a0000001-0000-0000-0000-000000000007'), -- LCS <- Strings
    -- Greedy
    ('a0000001-0000-0000-0000-000000000050', 'a0000001-0000-0000-0000-000000000004'), -- Greedy <- Time Complexity
    ('a0000001-0000-0000-0000-000000000050', 'a0000001-0000-0000-0000-000000000022')  -- Greedy <- Merge Sort
) AS v("ConceptId", "RequiredConceptId")
ON CONFLICT ("ConceptId", "RequiredConceptId") DO NOTHING;

COMMIT;

-- Post-run sanity checks (uncomment to verify):
-- SELECT COUNT(*) AS concept_count FROM "Concepts";            -- expect 50
-- SELECT COUNT(*) AS edge_count FROM "Prerequisites";          -- expect 71
-- SELECT COUNT(*) AS bad_edges FROM "Prerequisites" p
--   WHERE NOT EXISTS (SELECT 1 FROM "Concepts" c WHERE c."Id" = p."ConceptId")
--      OR NOT EXISTS (SELECT 1 FROM "Concepts" c WHERE c."Id" = p."RequiredConceptId");  -- expect 0
-- SELECT COUNT(*) AS self_edges FROM "Prerequisites"
--   WHERE "ConceptId" = "RequiredConceptId";                   -- expect 0
