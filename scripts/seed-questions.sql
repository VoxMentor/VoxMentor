-- scripts/seed-questions.sql
-- Seeds 100 practice questions (2 per concept × 50 DSA concepts).
-- Idempotent: safe to run multiple times (ON CONFLICT DO NOTHING).
--
-- Run order: seed-dsa-concepts.sql FIRST, then this file.
--
-- Concept UUIDs: a0000001-0000-0000-0000-000000000001 through ...0050
-- Each question has: title, description, difficulty, test cases (1 example + 1 hidden),
-- example inputs/outputs, starter code.

BEGIN;

INSERT INTO "Questions" ("Id", "ConceptId", "Title", "Description", "Difficulty", "TestCases", "ExampleInputs", "ExampleOutputs", "StarterCode", "HiddenTestCaseCount", "CreatedAt") VALUES

-- ======================================================================
-- Fundamentals (1-5): 2 questions each = 10 questions
-- ======================================================================

-- Concept 1: Variables and Data Types
('b0000001-0000-0000-0000-000000000001', 'a0000001-0000-0000-0000-000000000001',
 'Swap Two Variables',
 'Given two integers a and b, swap their values without using a temporary variable. Return both values.',
 1,
 ARRAY['{"input":"5 3","expected":"3 5","hidden":false}','{"input":"-1 7","expected":"7 -1","hidden":true}'],
 ARRAY['5 3'],
 ARRAY['3 5'],
 ARRAY['def swap(a, b):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000002', 'a0000001-0000-0000-0000-000000000001',
 'Type Checker',
 'Given a value, return its type as a string: "int", "float", "str", "bool", or "list".',
 1,
 ARRAY['{"input":"42","expected":"int","hidden":false}','{"input":"hello","expected":"str","hidden":true}'],
 ARRAY['42'],
 ARRAY['int'],
 ARRAY['def check_type(val):'],
 1, 1, now()),

-- Concept 2: Control Flow
('b0000001-0000-0000-0000-000000000003', 'a0000001-0000-0000-0000-000000000002',
 'FizzBuzz',
 'Return a list of strings from 1 to n. For multiples of 3 use "Fizz", for 5 use "Buzz", for both use "FizzBuzz".',
 1,
 ARRAY['{"input":"5","expected":"[\"1\",\"2\",\"Fizz\",\"4\",\"Buzz\"]","hidden":false}','{"input":"3","expected":"[\"1\",\"2\",\"Fizz\"]","hidden":true}'],
 ARRAY['5'],
 ARRAY['["1","2","Fizz","4","Buzz"]'],
 ARRAY['def fizzbuzz(n):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000004', 'a0000001-0000-0000-0000-000000000002',
 'Grade Classifier',
 'Given a numeric score (0-100), return the letter grade: A (90+), B (80-89), C (70-79), D (60-69), F (below 60).',
 1,
 ARRAY['{"input":"95","expected":"A","hidden":false}','{"input":"55","expected":"F","hidden":true}'],
 ARRAY['95'],
 ARRAY['A'],
 ARRAY['def classify(score):'],
 1, 1, now()),

-- Concept 3: Functions
('b0000001-0000-0000-0000-000000000005', 'a0000001-0000-0000-0000-000000000003',
 'Factorial',
 'Compute the factorial of a non-negative integer n. Return 1 for n=0.',
 2,
 ARRAY['{"input":"5","expected":"120","hidden":false}','{"input":"0","expected":"1","hidden":true}'],
 ARRAY['5'],
 ARRAY['120'],
 ARRAY['def factorial(n):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000006', 'a0000001-0000-0000-0000-000000000003',
 'Fibonacci',
 'Return the nth Fibonacci number (0-indexed: fib(0)=0, fib(1)=1).',
 2,
 ARRAY['{"input":"6","expected":"8","hidden":false}','{"input":"1","expected":"1","hidden":true}'],
 ARRAY['6'],
 ARRAY['8'],
 ARRAY['def fibonacci(n):'],
 1, 1, now()),

-- Concept 4: Time Complexity
('b0000001-0000-0000-0000-000000000007', 'a0000001-0000-0000-0000-000000000004',
 'Linear Search',
 'Search for a target in an array. Return the index if found, -1 otherwise. State the time complexity.',
 1,
 ARRAY['{"input":"[1,2,3,4,5] 3","expected":"2","hidden":false}','{"input":"[10,20,30] 5","expected":"-1","hidden":true}'],
 ARRAY['[1,2,3,4,5] 3'],
 ARRAY['2'],
 ARRAY['def linear_search(arr, target):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000008', 'a0000001-0000-0000-0000-000000000004',
 'Nested Loop Analysis',
 'Given n, count how many times the inner loop runs in this pattern: for i in range(n): for j in range(i): count++. Return the count.',
 2,
 ARRAY['{"input":"4","expected":"6","hidden":false}','{"input":"5","expected":"10","hidden":true}'],
 ARRAY['4'],
 ARRAY['6'],
 ARRAY['def count_iterations(n):'],
 1, 1, now()),

-- Concept 5: Space Complexity
('b0000001-0000-0000-0000-000000000009', 'a0000001-0000-0000-0000-000000000005',
 'Reverse Array In-Place',
 'Reverse an array in-place without using extra space. Return the reversed array.',
 2,
 ARRAY['{"input":"[1,2,3,4,5]","expected":"[5,4,3,2,1]","hidden":false}','{"input":"[10,20]","expected":"[20,10]","hidden":true}'],
 ARRAY['[1,2,3,4,5]'],
 ARRAY['[5,4,3,2,1]'],
 ARRAY['def reverse_array(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000010', 'a0000001-0000-0000-0000-000000000005',
 'Sum Without Extra Space',
 'Given an array, return the sum using O(1) extra space (just a running total variable).',
 1,
 ARRAY['{"input":"[1,2,3,4,5]","expected":"15","hidden":false}','{"input":"[-1,1]","expected":"0","hidden":true}'],
 ARRAY['[1,2,3,4,5]'],
 ARRAY['15'],
 ARRAY['def array_sum(arr):'],
 1, 1, now()),

-- ======================================================================
-- Data Structures (6-13): 2 questions each = 16 questions
-- ======================================================================

-- Concept 6: Arrays
('b0000001-0000-0000-0000-000000000011', 'a0000001-0000-0000-0000-000000000006',
 'Find Maximum',
 'Find the maximum element in an array of integers.',
 1,
 ARRAY['{"input":"[3,7,2,9,1]","expected":"9","hidden":false}','{"input":"[-5,-1,-8]","expected":"-1","hidden":true}'],
 ARRAY['[3,7,2,9,1]'],
 ARRAY['9'],
 ARRAY['def find_max(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000012', 'a0000001-0000-0000-0000-000000000006',
 'Remove Duplicates',
 'Remove duplicates from a sorted array in-place. Return the new length.',
 2,
 ARRAY['{"input":"[1,1,2,3,3]","expected":"3","hidden":false}','{"input":"[1,2,3]","expected":"3","hidden":true}'],
 ARRAY['[1,1,2,3,3]'],
 ARRAY['3'],
 ARRAY['def remove_duplicates(arr):'],
 1, 1, now()),

-- Concept 7: Strings
('b0000001-0000-0000-0000-000000000013', 'a0000001-0000-0000-0000-000000000007',
 'Reverse String',
 'Reverse a string and return the result.',
 1,
 ARRAY['{"input":"hello","expected":"olleh","hidden":false}','{"input":"abc","expected":"cba","hidden":true}'],
 ARRAY['hello'],
 ARRAY['olleh'],
 ARRAY['def reverse_string(s):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000014', 'a0000001-0000-0000-0000-000000000007',
 'Count Vowels',
 'Count the number of vowels (a, e, i, o, u) in a string. Case-insensitive.',
 1,
 ARRAY['{"input":"hello","expected":"2","hidden":false}','{"input":"AEIOU","expected":"5","hidden":true}'],
 ARRAY['hello'],
 ARRAY['2'],
 ARRAY['def count_vowels(s):'],
 1, 1, now()),

-- Concept 8: Hash Maps and Sets
('b0000001-0000-0000-0000-000000000015', 'a0000001-0000-0000-0000-000000000008',
 'Two Sum',
 'Given an array of integers and a target, return indices of two numbers that add up to the target.',
 2,
 ARRAY['{"input":"[2,7,11,15] 9","expected":"[0,1]","hidden":false}','{"input":"[3,2,4] 6","expected":"[1,2]","hidden":true}'],
 ARRAY['[2,7,11,15] 9'],
 ARRAY['[0,1]'],
 ARRAY['def two_sum(nums, target):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000016', 'a0000001-0000-0000-0000-000000000008',
 'Frequency Counter',
 'Given a string, return a dictionary of character frequencies.',
 1,
 ARRAY['{"input":"aab","expected":"{\"a\":2,\"b\":1}","hidden":false}','{"input":"abc","expected":"{\"a\":1,\"b\":1,\"c\":1}","hidden":true}'],
 ARRAY['aab'],
 ARRAY['{"a":2,"b":1}'],
 ARRAY['def frequency(s):'],
 1, 1, now()),

-- Concept 9: Singly Linked Lists
('b0000001-0000-0000-0000-000000000017', 'a0000001-0000-0000-0000-000000000009',
 'Reverse Linked List',
 'Given the head of a singly linked list, reverse it and return the new head.',
 2,
 ARRAY['{"input":"[1,2,3,4,5]","expected":"[5,4,3,2,1]","hidden":false}','{"input":"[1,2]","expected":"[2,1]","hidden":true}'],
 ARRAY['[1,2,3,4,5]'],
 ARRAY['[5,4,3,2,1]'],
 ARRAY['class ListNode:','    def __init__(self, val=0, next=None):','        self.val = val','        self.next = next','def reverse_list(head):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000018', 'a0000001-0000-0000-0000-000000000009',
 'Merge Two Sorted Lists',
 'Merge two sorted linked lists into one sorted list.',
 2,
 ARRAY['{"input":"[1,2,4] [1,3,4]","expected":"[1,1,2,3,4,4]","hidden":false}','{"input":"[] [0]","expected":"[0]","hidden":true}'],
 ARRAY['[1,2,4] [1,3,4]'],
 ARRAY['[1,1,2,3,4,4]'],
 ARRAY['def merge_lists(l1, l2):'],
 1, 1, now()),

-- Concept 10: Doubly Linked Lists
('b0000001-0000-0000-0000-000000000019', 'a0000001-0000-0000-0000-000000000010',
 'LRU Cache',
 'Design an LRU cache with get and put operations in O(1) time.',
 3,
 ARRAY['{"input":"put(1,1) put(2,2) get(1) put(3,3) get(2)","expected":"1 -1","hidden":false}','{"input":"put(1,1) put(2,2) get(1) get(2)","expected":"1 2","hidden":true}'],
 ARRAY['put(1,1) put(2,2) get(1) put(3,3) get(2)'],
 ARRAY['1 -1'],
 ARRAY['class LRUCache:','    def __init__(self, capacity):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000020', 'a0000001-0000-0000-0000-000000000010',
 'Dequeue Implementation',
 'Implement a double-ended queue supporting add_front, add_rear, remove_front, remove_rear.',
 2,
 ARRAY['{"input":"add_rear(1) add_rear(2) remove_front() remove_rear()","expected":"1 2","hidden":false}','{"input":"add_front(3) add_rear(4) remove_front() remove_rear()","expected":"3 4","hidden":true}'],
 ARRAY['add_rear(1) add_rear(2) remove_front() remove_rear()'],
 ARRAY['1 2'],
 ARRAY['class Deque:','    def __init__(self):'],
 1, 1, now()),

-- Concept 11: Stacks
('b0000001-0000-0000-0000-000000000021', 'a0000001-0000-0000-0000-000000000011',
 'Valid Parentheses',
 'Given a string of parentheses, check if they are valid (each open has a matching close in the correct order).',
 2,
 ARRAY['{"input":"()[]{}","expected":"true","hidden":false}','{"input":"(]","expected":"false","hidden":true}'],
 ARRAY['()[]{}'],
 ARRAY['true'],
 ARRAY['def is_valid(s):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000022', 'a0000001-0000-0000-0000-000000000011',
 'Min Stack',
 'Design a stack that supports push, pop, top, and retrieving the minimum element in O(1).',
 3,
 ARRAY['{"input":"push(-2) push(0) push(-3) getMin() pop() top() getMin()","expected":"-3 0 -2","hidden":false}','{"input":"push(1) push(2) getMin() pop() getMin()","expected":"1 1","hidden":true}'],
 ARRAY['push(-2) push(0) push(-3) getMin() pop() top() getMin()'],
 ARRAY['-3 0 -2'],
 ARRAY['class MinStack:','    def __init__(self):'],
 1, 1, now()),

-- Concept 12: Queues
('b0000001-0000-0000-0000-000000000023', 'a0000001-0000-0000-0000-000000000012',
 'Implement Queue using Stacks',
 'Implement a FIFO queue using two stacks.',
 3,
 ARRAY['{"input":"enqueue(1) enqueue(2) dequeue() enqueue(3) dequeue() dequeue()","expected":"1 2 3","hidden":false}','{"input":"enqueue(1) dequeue() enqueue(2) dequeue()","expected":"1 2","hidden":true}'],
 ARRAY['enqueue(1) enqueue(2) dequeue() enqueue(3) dequeue() dequeue()'],
 ARRAY['1 2 3'],
 ARRAY['class QueueFromStacks:','    def __init__(self):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000024', 'a0000001-0000-0000-0000-000000000012',
 'BFS Level Order',
 'Given a binary tree, return level-order traversal as a list of lists.',
 2,
 ARRAY['{"input":"[3,9,20,null,null,15,7]","expected":"[[3],[9,20],[15,7]]","hidden":false}','{"input":"[1]","expected":"[[1]]","hidden":true}'],
 ARRAY['[3,9,20,null,null,15,7]'],
 ARRAY['[[3],[9,20],[15,7]]'],
 ARRAY['def level_order(root):'],
 1, 1, now()),

-- Concept 13: Deque
('b0000001-0000-0000-0000-000000000025', 'a0000001-0000-0000-0000-000000000013',
 'Sliding Window Maximum',
 'Given an array and window size k, find the maximum in each sliding window.',
 3,
 ARRAY['{"input":"[1,3,-1,-3,5,3,6,7] 3","expected":"[3,3,5,5,6,7]","hidden":false}','{"input":"[1] 1","expected":"[1]","hidden":true}'],
 ARRAY['[1,3,-1,-3,5,3,6,7] 3'],
 ARRAY['[3,3,5,5,6,7]'],
 ARRAY['def sliding_max(nums, k):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000026', 'a0000001-0000-0000-0000-000000000013',
 'Palindrome Deque',
 'Check if a string is a palindrome using a deque.',
 2,
 ARRAY['{"input":"racecar","expected":"true","hidden":false}','{"input":"hello","expected":"false","hidden":true}'],
 ARRAY['racecar'],
 ARRAY['true'],
 ARRAY['def is_palindrome(s):'],
 1, 1, now()),

-- ======================================================================
-- Algorithms: Recursion (14)
-- ======================================================================

('b0000001-0000-0000-0000-000000000027', 'a0000001-0000-0000-0000-000000000014',
 'Power Calculation',
 'Compute x raised to the power n using recursion. Handle negative exponents.',
 2,
 ARRAY['{"input":"2 3","expected":"8","hidden":false}','{"input":"2 -1","expected":"0.5","hidden":true}'],
 ARRAY['2 3'],
 ARRAY['8'],
 ARRAY['def power(x, n):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000028', 'a0000001-0000-0000-0000-000000000014',
 'String Permutations',
 'Generate all permutations of a given string. Return them sorted.',
 3,
 ARRAY['{"input":"abc","expected":"[\"abc\",\"acb\",\"bac\",\"bca\",\"cab\",\"cba\"]","hidden":false}','{"input":"ab","expected":"[\"ab\",\"ba\"]","hidden":true}'],
 ARRAY['abc'],
 ARRAY['["abc","acb","bac","bca","cab","cba"]'],
 ARRAY['def permutations(s):'],
 1, 1, now()),

-- ======================================================================
-- Searching (15)
-- ======================================================================

('b0000001-0000-0000-0000-000000000029', 'a0000001-0000-0000-0000-000000000015',
 'Binary Search',
 'Implement binary search on a sorted array. Return the index or -1.',
 2,
 ARRAY['{"input":"[1,2,3,4,5,6,7] 4","expected":"3","hidden":false}','{"input":"[1,2,3,4,5] 6","expected":"-1","hidden":true}'],
 ARRAY['[1,2,3,4,5,6,7] 4'],
 ARRAY['3'],
 ARRAY['def binary_search(arr, target):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000030', 'a0000001-0000-0000-0000-000000000015',
 'First Bad Version',
 'Find the first bad version in a sequence. is_bad_version(v) returns true if v is bad.',
 2,
 ARRAY['{"input":"5 with bad=4","expected":"4","hidden":false}','{"input":"3 with bad=1","expected":"1","hidden":true}'],
 ARRAY['5 with bad=4'],
 ARRAY['4'],
 ARRAY['def first_bad_version(n, is_bad):'],
 1, 1, now()),

-- ======================================================================
-- Techniques (16-18)
-- ======================================================================

-- Concept 16: Two Pointers
('b0000001-0000-0000-0000-000000000031', 'a0000001-0000-0000-0000-000000000016',
 'Two Sum Sorted',
 'Given a sorted array, find two numbers that add up to target. Return 1-indexed indices.',
 2,
 ARRAY['{"input":"[2,7,11,15] 9","expected":"[1,2]","hidden":false}','{"input":"[2,3,4] 6","expected":"[1,3]","hidden":true}'],
 ARRAY['[2,7,11,15] 9'],
 ARRAY['[1,2]'],
 ARRAY['def two_sum_sorted(numbers, target):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000032', 'a0000001-0000-0000-0000-000000000016',
 'Container With Most Water',
 'Find two lines that together with the x-axis form a container holding the most water.',
 3,
 ARRAY['{"input":"[1,8,6,2,5,4,8,3,7]","expected":"49","hidden":false}','{"input":"[1,1]","expected":"1","hidden":true}'],
 ARRAY['[1,8,6,2,5,4,8,3,7]'],
 ARRAY['49'],
 ARRAY['def max_area(height):'],
 1, 1, now()),

-- Concept 17: Sliding Window
('b0000001-0000-0000-0000-000000000033', 'a0000001-0000-0000-0000-000000000017',
 'Max Sum Subarray of Size K',
 'Find the maximum sum of any contiguous subarray of size k.',
 2,
 ARRAY['{"input":"[2,1,5,1,3,2] 3","expected":"9","hidden":false}','{"input":"[1,4,2,10,23,3,1,0,20] 4","expected":"39","hidden":true}'],
 ARRAY['[2,1,5,1,3,2] 3'],
 ARRAY['9'],
 ARRAY['def max_sum_subarray(arr, k):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000034', 'a0000001-0000-0000-0000-000000000017',
 'Longest Substring Without Repeating',
 'Find the length of the longest substring without repeating characters.',
 3,
 ARRAY['{"input":"abcabcbb","expected":"3","hidden":false}','{"input":"bbbbb","expected":"1","hidden":true}'],
 ARRAY['abcabcbb'],
 ARRAY['3'],
 ARRAY['def length_of_longest_substring(s):'],
 1, 1, now()),

-- Concept 18: Prefix Sum
('b0000001-0000-0000-0000-000000000035', 'a0000001-0000-0000-0000-000000000018',
 'Range Sum Query',
 'Given an array, answer multiple queries about the sum of elements between indices i and j.',
 2,
 ARRAY['{"input":"[1,2,3,4,5] queries=[[0,2],[1,3]]","expected":"[6,9]","hidden":false}','{"input":"[1,2,3] queries=[[0,0]]","expected":"[1]","hidden":true}'],
 ARRAY['[1,2,3,4,5] queries=[[0,2],[1,3]]'],
 ARRAY['[6,9]'],
 ARRAY['class NumArray:','    def __init__(self, nums):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000036', 'a0000001-0000-0000-0000-000000000018',
 'Subarray Sum Equals K',
 'Count the number of subarrays whose sum equals k.',
 3,
 ARRAY['{"input":"[1,1,1] 2","expected":"2","hidden":false}','{"input":"[1,2,3] 3","expected":"2","hidden":true}'],
 ARRAY['[1,1,1] 2'],
 ARRAY['2'],
 ARRAY['def subarray_sum(nums, k):'],
 1, 1, now()),

-- ======================================================================
-- Sorting (19-24)
-- ======================================================================

-- Concept 19: Bubble Sort
('b0000001-0000-0000-0000-000000000037', 'a0000001-0000-0000-0000-000000000019',
 'Bubble Sort Implementation',
 'Implement bubble sort. Return the sorted array.',
 2,
 ARRAY['{"input":"[64,34,25,12,22,11,90]","expected":"[11,12,22,25,34,64,90]","hidden":false}','{"input":"[5,1,4,2,8]","expected":"[1,2,4,5,8]","hidden":true}'],
 ARRAY['[64,34,25,12,22,11,90]'],
 ARRAY['[11,12,22,25,34,64,90]'],
 ARRAY['def bubble_sort(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000038', 'a0000001-0000-0000-0000-000000000019',
 'Sort Colors',
 'Sort an array containing only 0s, 1s, and 2s in-place (Dutch National Flag).',
 2,
 ARRAY['{"input":"[2,0,2,1,1,0]","expected":"[0,0,1,1,2,2]","hidden":false}','{"input":"[0,1,0]","expected":"[0,0,1]","hidden":true}'],
 ARRAY['[2,0,2,1,1,0]'],
 ARRAY['[0,0,1,1,2,2]'],
 ARRAY['def sort_colors(nums):'],
 1, 1, now()),

-- Concept 20: Selection Sort
('b0000001-0000-0000-0000-000000000039', 'a0000001-0000-0000-0000-000000000020',
 'Selection Sort Implementation',
 'Implement selection sort. Return the sorted array.',
 2,
 ARRAY['{"input":"[64,25,12,22,11]","expected":"[11,12,22,25,64]","hidden":false}','{"input":"[5,3,1,2]","expected":"[1,2,3,5]","hidden":true}'],
 ARRAY['[64,25,12,22,11]'],
 ARRAY['[11,12,22,25,64]'],
 ARRAY['def selection_sort(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000040', 'a0000001-0000-0000-0000-000000000020',
 'Kth Smallest Element',
 'Find the kth smallest element in an unsorted array using selection.',
 2,
 ARRAY['{"input":"[7,10,4,3,20,15] 3","expected":"7","hidden":false}','{"input":"[7,10,4,3,20,15] 6","expected":"20","hidden":true}'],
 ARRAY['[7,10,4,3,20,15] 3'],
 ARRAY['7'],
 ARRAY['def kth_smallest(arr, k):'],
 1, 1, now()),

-- Concept 21: Insertion Sort
('b0000001-0000-0000-0000-000000000041', 'a0000001-0000-0000-0000-000000000021',
 'Insertion Sort Implementation',
 'Implement insertion sort. Return the sorted array.',
 2,
 ARRAY['{"input":"[12,11,13,5,6]","expected":"[5,6,11,12,13]","hidden":false}','{"input":"[3,1,4,1,5]","expected":"[1,1,3,4,5]","hidden":true}'],
 ARRAY['[12,11,13,5,6]'],
 ARRAY['[5,6,11,12,13]'],
 ARRAY['def insertion_sort(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000042', 'a0000001-0000-0000-0000-000000000021',
 'Insert into Sorted List',
 'Given a sorted list and a new element, insert it in the correct position.',
 2,
 ARRAY['{"input":"[1,3,5,7] 4","expected":"[1,3,4,5,7]","hidden":false}','{"input":"[1,2,3] 0","expected":"[0,1,2,3]","hidden":true}'],
 ARRAY['[1,3,5,7] 4'],
 ARRAY['[1,3,4,5,7]'],
 ARRAY['def insert_sorted(arr, val):'],
 1, 1, now()),

-- Concept 22: Merge Sort
('b0000001-0000-0000-0000-000000000043', 'a0000001-0000-0000-0000-000000000022',
 'Merge Sort Implementation',
 'Implement merge sort. Return the sorted array.',
 3,
 ARRAY['{"input":"[38,27,43,3,9,82,10]","expected":"[3,9,10,27,38,43,82]","hidden":false}','{"input":"[5,2,3,1]","expected":"[1,2,3,5]","hidden":true}'],
 ARRAY['[38,27,43,3,9,82,10]'],
 ARRAY['[3,9,10,27,38,43,82]'],
 ARRAY['def merge_sort(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000044', 'a0000001-0000-0000-0000-000000000022',
 'Merge Sorted Arrays',
 'Merge two sorted arrays into one sorted array.',
 2,
 ARRAY['{"input":"[1,3,5] [2,4,6]","expected":"[1,2,3,4,5,6]","hidden":false}','{"input":"[1,2] [3,4,5]","expected":"[1,2,3,4,5]","hidden":true}'],
 ARRAY['[1,3,5] [2,4,6]'],
 ARRAY['[1,2,3,4,5,6]'],
 ARRAY['def merge(a, b):'],
 1, 1, now()),

-- Concept 23: Quick Sort
('b0000001-0000-0000-0000-000000000045', 'a0000001-0000-0000-0000-000000000023',
 'Quick Sort Implementation',
 'Implement quick sort using Lomuto partition. Return the sorted array.',
 3,
 ARRAY['{"input":"[10,7,8,9,1,5]","expected":"[1,5,7,8,9,10]","hidden":false}','{"input":"[3,3,3]","expected":"[3,3,3]","hidden":true}'],
 ARRAY['[10,7,8,9,1,5]'],
 ARRAY['[1,5,7,8,9,10]'],
 ARRAY['def quick_sort(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000046', 'a0000001-0000-0000-0000-000000000023',
 'Kth Largest Element',
 'Find the kth largest element using quickselect.',
 3,
 ARRAY['{"input":"[3,2,1,5,6,4] 2","expected":"5","hidden":false}','{"input":"[3,2,3,1,2,4,5,5,6] 4","expected":"4","hidden":true}'],
 ARRAY['[3,2,1,5,6,4] 2'],
 ARRAY['5'],
 ARRAY['def kth_largest(arr, k):'],
 1, 1, now()),

-- Concept 24: Counting Sort
('b0000001-0000-0000-0000-000000000047', 'a0000001-0000-0000-0000-000000000024',
 'Counting Sort Implementation',
 'Implement counting sort for non-negative integers.',
 2,
 ARRAY['{"input":"[4,2,2,8,3,3,1]","expected":"[1,2,2,3,3,4,8]","hidden":false}','{"input":"[5,5,5]","expected":"[5,5,5]","hidden":true}'],
 ARRAY['[4,2,2,8,3,3,1]'],
 ARRAY['[1,2,2,3,3,4,8]'],
 ARRAY['def counting_sort(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000048', 'a0000001-0000-0000-0000-000000000024',
 'Sort Characters By Frequency',
 'Sort characters of a string by frequency (highest first).',
 2,
 ARRAY['{"input":"tree","expected":"eert","hidden":false}','{"input":"cccaaa","expected":"aaaccc","hidden":true}'],
 ARRAY['tree'],
 ARRAY['eert'],
 ARRAY['def frequency_sort(s):'],
 1, 1, now()),

-- ======================================================================
-- Trees (25-30)
-- ======================================================================

-- Concept 25: Binary Trees
('b0000001-0000-0000-0000-000000000049', 'a0000001-0000-0000-0000-000000000025',
 'Maximum Depth of Binary Tree',
 'Find the maximum depth (height) of a binary tree.',
 2,
 ARRAY['{"input":"[3,9,20,null,null,15,7]","expected":"3","hidden":false}','{"input":"[1,null,2]","expected":"2","hidden":true}'],
 ARRAY['[3,9,20,null,null,15,7]'],
 ARRAY['3'],
 ARRAY['def max_depth(root):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000050', 'a0000001-0000-0000-0000-000000000025',
 'Symmetric Tree',
 'Check if a binary tree is a mirror of itself.',
 2,
 ARRAY['{"input":"[1,2,2,3,4,4,3]","expected":"true","hidden":false}','{"input":"[1,2,2,null,3,null,3]","expected":"false","hidden":true}'],
 ARRAY['[1,2,2,3,4,4,3]'],
 ARRAY['true'],
 ARRAY['def is_symmetric(root):'],
 1, 1, now()),

-- Concept 26: Binary Search Trees
('b0000001-0000-0000-0000-000000000051', 'a0000001-0000-0000-0000-000000000026',
 'Validate BST',
 'Check if a binary tree is a valid Binary Search Tree.',
 3,
 ARRAY['{"input":"[2,1,3]","expected":"true","hidden":false}','{"input":"[5,1,4,null,null,3,6]","expected":"false","hidden":true}'],
 ARRAY['[2,1,3]'],
 ARRAY['true'],
 ARRAY['def is_valid_bst(root):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000052', 'a0000001-0000-0000-0000-000000000026',
 'Search in BST',
 'Search for a value in a BST and return the node.',
 2,
 ARRAY['{"input":"[4,2,7,1,3] 2","expected":"true","hidden":false}','{"input":"[4,2,7,1,3] 5","expected":"false","hidden":true}'],
 ARRAY['[4,2,7,1,3] 2'],
 ARRAY['true'],
 ARRAY['def search_bst(root, val):'],
 1, 1, now()),

-- Concept 27: Tree Traversals
('b0000001-0000-0000-0000-000000000053', 'a0000001-0000-0000-0000-000000000027',
 'Inorder Traversal',
 'Perform inorder traversal of a binary tree (left, root, right).',
 2,
 ARRAY['{"input":"[1,null,2,3]","expected":"[1,3,2]","hidden":false}','{"input":"[2,1,3]","expected":"[1,2,3]","hidden":true}'],
 ARRAY['[1,null,2,3]'],
 ARRAY['[1,3,2]'],
 ARRAY['def inorder(root):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000054', 'a0000001-0000-0000-0000-000000000027',
 'Construct from Traversals',
 'Given preorder and inorder traversals, construct the binary tree.',
 3,
 ARRAY['{"input":"[3,9,20,15,7] [9,3,15,20,7]","expected":"[3,9,20,null,null,15,7]","hidden":false}','{"input":"[1,2] [2,1]","expected":"[1,2]","hidden":true}'],
 ARRAY['[3,9,20,15,7] [9,3,15,20,7]'],
 ARRAY['[3,9,20,null,null,15,7]'],
 ARRAY['def build_tree(preorder, inorder):'],
 1, 1, now()),

-- Concept 28: Heap Data Structure
('b0000001-0000-0000-0000-000000000055', 'a0000001-0000-0000-0000-000000000028',
 'Build Min Heap',
 'Given an array, build a min heap and return the heapified array.',
 3,
 ARRAY['{"input":"[3,1,2]","expected":"[1,3,2]","hidden":false}','{"input":"[5,4,3,2,1]","expected":"[1,2,3,5,4]","hidden":true}'],
 ARRAY['[3,1,2]'],
 ARRAY['[1,3,2]'],
 ARRAY['def build_min_heap(arr):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000056', 'a0000001-0000-0000-0000-000000000028',
 'Heap Sort',
 'Implement heap sort. Return the sorted array.',
 3,
 ARRAY['{"input":"[12,11,13,5,6,7]","expected":"[5,6,7,11,12,13]","hidden":false}','{"input":"[3,1,4,1,5]","expected":"[1,1,3,4,5]","hidden":true}'],
 ARRAY['[12,11,13,5,6,7]'],
 ARRAY['[5,6,7,11,12,13]'],
 ARRAY['def heap_sort(arr):'],
 1, 1, now()),

-- Concept 29: Priority Queues
('b0000001-0000-0000-0000-000000000057', 'a0000001-0000-0000-0000-000000000029',
 'Top K Frequent Elements',
 'Find the k most frequent elements in an array.',
 2,
 ARRAY['{"input":"[1,1,1,2,2,3] 2","expected":"[1,2]","hidden":false}','{"input":"[1] 1","expected":"[1]","hidden":true}'],
 ARRAY['[1,1,1,2,2,3] 2'],
 ARRAY['[1,2]'],
 ARRAY['def top_k_frequent(nums, k):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000058', 'a0000001-0000-0000-0000-000000000029',
 'Median of Data Stream',
 'Design a data structure that supports add_num and find_median.',
 4,
 ARRAY['{"input":"add(1) add(2) median() add(3) median()","expected":"1.5 2","hidden":false}','{"input":"add(5) median() add(3) median()","expected":"5 4","hidden":true}'],
 ARRAY['add(1) add(2) median() add(3) median()'],
 ARRAY['1.5 2'],
 ARRAY['class MedianFinder:','    def __init__(self):'],
 1, 1, now()),

-- Concept 30: Trie
('b0000001-0000-0000-0000-000000000059', 'a0000001-0000-0000-0000-000000000030',
 'Implement Trie',
 'Implement a trie with insert, search, and startsWith operations.',
 3,
 ARRAY['{"input":"insert(\"apple\") search(\"apple\") search(\"app\") startsWith(\"app\")","expected":"true false true","hidden":false}','{"input":"insert(\"cat\") search(\"cat\") search(\"car\")","expected":"true false","hidden":true}'],
 ARRAY['insert("apple") search("apple") search("app") startsWith("app")'],
 ARRAY['true false true'],
 ARRAY['class Trie:','    def __init__(self):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000060', 'a0000001-0000-0000-0000-000000000030',
 'Word Search II',
 'Find all words from a dictionary that exist in a 2D character board.',
 4,
 ARRAY['{"input":"board=[[\"o\",\"a\",\"a\",\"n\"],[\"e\",\"t\",\"a\",\"e\"],[\"i\",\"h\",\"k\",\"r\"],[\"i\",\"f\",\"l\",\"v\"]] words=[\"oath\",\"pea\",\"eat\",\"rain\"]","expected":"[\"eat\",\"oath\"]","hidden":false}','{"input":"board=[[\"a\",\"b\"]] words=[\"ab\"]","expected":"[\"ab\"]","hidden":true}'],
 ARRAY['board=[["o","a","a","n"],["e","t","a","e"],["i","h","k","r"],["i","f","l","v"]] words=["oath","pea","eat","rain"]'],
 ARRAY['["eat","oath"]'],
 ARRAY['def find_words(board, words):'],
 1, 1, now()),

-- ======================================================================
-- Graphs (31-44): 2 questions each = 28 questions
-- ======================================================================

-- Concept 31: Graph Representations
('b0000001-0000-0000-0000-000000000061', 'a0000001-0000-0000-0000-000000000031',
 'Adjacency List Implementation',
 'Implement a graph using adjacency list. Support addVertex, addEdge, getNeighbors.',
 2,
 ARRAY['{"input":"add(0) add(1) edge(0,1) neighbors(0)","expected":"[1]","hidden":false}','{"input":"add(0) add(1) add(2) edge(0,1) edge(0,2) neighbors(0)","expected":"[1,2]","hidden":true}'],
 ARRAY['add(0) add(1) edge(0,1) neighbors(0)'],
 ARRAY['[1]'],
 ARRAY['class Graph:','    def __init__(self):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000062', 'a0000001-0000-0000-0000-000000000031',
 'Adjacency Matrix Implementation',
 'Implement a graph using adjacency matrix. Support addEdge, hasEdge.',
 2,
 ARRAY['{"input":"add(0,1) has(0,1) has(1,0)","expected":"true true","hidden":false}','{"input":"add(0,1) add(1,2) has(0,2)","expected":"false","hidden":true}'],
 ARRAY['add(0,1) has(0,1) has(1,0)'],
 ARRAY['true true'],
 ARRAY['class GraphMatrix:','    def __init__(self, n):'],
 1, 1, now()),

-- Concept 32: BFS
('b0000001-0000-0000-0000-000000000063', 'a0000001-0000-0000-0000-000000000032',
 'BFS Traversal',
 'Perform BFS on a graph starting from a given node. Return visited nodes in order.',
 2,
 ARRAY['{"input":"adj={0:[1,2],1:[2],2:[]} start=0","expected":"[0,1,2]","hidden":false}','{"input":"adj={0:[1],1:[2],2:[]} start=0","expected":"[0,1,2]","hidden":true}'],
 ARRAY['adj={0:[1,2],1:[2],2:[]} start=0'],
 ARRAY['[0,1,2]'],
 ARRAY['def bfs(graph, start):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000064', 'a0000001-0000-0000-0000-000000000032',
 'Shortest Path Unweighted',
 'Find the shortest path between two nodes in an unweighted graph.',
 2,
 ARRAY['{"input":"adj={0:[1,3],1:[0,2],2:[1,3],3:[0,2]} start=0 end=2","expected":"2","hidden":false}','{"input":"adj={0:[1],1:[2],2:[]} start=0 end=2","expected":"2","hidden":true}'],
 ARRAY['adj={0:[1,3],1:[0,2],2:[1,3],3:[0,2]} start=0 end=2'],
 ARRAY['2'],
 ARRAY['def shortest_path(graph, start, end):'],
 1, 1, now()),

-- Concept 33: DFS
('b0000001-0000-0000-0000-000000000065', 'a0000001-0000-0000-0000-000000000033',
 'DFS Traversal',
 'Perform DFS on a graph starting from a given node. Return visited nodes.',
 2,
 ARRAY['{"input":"adj={0:[1,2],1:[2],2:[]} start=0","expected":"[0,1,2]","hidden":false}','{"input":"adj={0:[1],1:[2],2:[]} start=0","expected":"[0,1,2]","hidden":true}'],
 ARRAY['adj={0:[1,2],1:[2],2:[]} start=0'],
 ARRAY['[0,1,2]'],
 ARRAY['def dfs(graph, start):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000066', 'a0000001-0000-0000-0000-000000000033',
 'Number of Islands',
 'Count the number of islands in a 2D grid (1=land, 0=water).',
 3,
 ARRAY['{"input":"[[\"1\",\"1\",\"1\",\"1\",\"0\"],[\"1\",\"1\",\"0\",\"1\",\"0\"],[\"1\",\"1\",\"0\",\"0\",\"0\"],[\"0\",\"0\",\"0\",\"0\",\"0\"]]","expected":"1","hidden":false}','{"input":"[[\"1\",\"1\",\"0\",\"0\",\"0\"],[\"1\",\"1\",\"0\",\"0\",\"0\"],[\"0\",\"0\",\"1\",\"0\",\"0\"],[\"0\",\"0\",\"0\",\"1\",\"1\"]]","expected":"3","hidden":true}'],
 ARRAY['[["1","1","1","1","0"],["1","1","0","1","0"],["1","1","0","0","0"],["0","0","0","0","0"]]'],
 ARRAY['1'],
 ARRAY['def num_islands(grid):'],
 1, 1, now()),

-- Concept 34: Topological Sort
('b0000001-0000-0000-0000-000000000067', 'a0000001-0000-0000-0000-000000000034',
 'Course Schedule',
 'Determine if you can finish all courses given prerequisites (detect cycle).',
 3,
 ARRAY['{"input":"2 [[1,0]]","expected":"true","hidden":false}','{"input":"2 [[1,0],[0,1]]","expected":"false","hidden":true}'],
 ARRAY['2 [[1,0]]'],
 ARRAY['true'],
 ARRAY['def can_finish(num, prerequisites):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000068', 'a0000001-0000-0000-0000-000000000034',
 'Topological Ordering',
 'Return a valid topological ordering of a DAG.',
 3,
 ARRAY['{"input":"4 [[1,0],[2,0],[3,1],[3,2]]","expected":"[0,1,2,3]","hidden":false}','{"input":"2 [[1,0]]","expected":"[0,1]","hidden":true}'],
 ARRAY['4 [[1,0],[2,0],[3,1],[3,2]]'],
 ARRAY['[0,1,2,3]'],
 ARRAY['def topo_sort(num, edges):'],
 1, 1, now()),

-- Concept 35: Cycle Detection
('b0000001-0000-0000-0000-000000000069', 'a0000001-0000-0000-0000-000000000035',
 'Detect Cycle in Directed Graph',
 'Check if a directed graph contains a cycle.',
 3,
 ARRAY['{"input":"3 [[0,1],[1,2]]","expected":"false","hidden":false}','{"input":"3 [[0,1],[1,2],[2,0]]","expected":"true","hidden":true}'],
 ARRAY['3 [[0,1],[1,2]]'],
 ARRAY['false'],
 ARRAY['def has_cycle(num, edges):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000070', 'a0000001-0000-0000-0000-000000000035',
 'Detect Cycle in Undirected Graph',
 'Check if an undirected graph contains a cycle.',
 3,
 ARRAY['{"input":"4 [[0,1],[1,2],[2,3]]","expected":"false","hidden":false}','{"input":"3 [[0,1],[1,2],[2,0]]","expected":"true","hidden":true}'],
 ARRAY['4 [[0,1],[1,2],[2,3]]'],
 ARRAY['false'],
 ARRAY['def has_cycle_undirected(num, edges):'],
 1, 1, now()),

-- Concept 36: Union-Find
('b0000001-0000-0000-0000-000000000071', 'a0000001-0000-0000-0000-000000000036',
 'Union-Find Implementation',
 'Implement Union-Find with path compression and union by rank.',
 3,
 ARRAY['{"input":"union(0,1) union(2,3) find(0)==find(1) find(0)==find(2)","expected":"true false","hidden":false}','{"input":"union(0,1) union(1,2) find(0)==find(2)","expected":"true","hidden":true}'],
 ARRAY['union(0,1) union(2,3) find(0)==find(1) find(0)==find(2)'],
 ARRAY['true false'],
 ARRAY['class UnionFind:','    def __init__(self, n):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000072', 'a0000001-0000-0000-0000-000000000036',
 'Number of Connected Components',
 'Count connected components in an undirected graph using Union-Find.',
 3,
 ARRAY['{"input":"5 [[0,1],[1,2],[3,4]]","expected":"2","hidden":false}','{"input":"3 [[0,1],[1,2]]","expected":"1","hidden":true}'],
 ARRAY['5 [[0,1],[1,2],[3,4]]'],
 ARRAY['2'],
 ARRAY['def count_components(n, edges):'],
 1, 1, now()),

-- Concept 37: Dijkstra's Algorithm
('b0000001-0000-0000-0000-000000000073', 'a0000001-0000-0000-0000-000000000037',
 'Dijkstra Shortest Path',
 'Find shortest path from source to all vertices in a weighted graph.',
 4,
 ARRAY['{"input":"4 [[0,1,1],[0,2,4],[1,2,2],[2,3,1]] src=0","expected":"[0,1,3,4]","hidden":false}','{"input":"3 [[0,1,2],[1,2,3]] src=0","expected":"[0,2,5]","hidden":true}'],
 ARRAY['4 [[0,1,1],[0,2,4],[1,2,2],[2,3,1]] src=0'],
 ARRAY['[0,1,3,4]'],
 ARRAY['def dijkstra(n, edges, src):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000074', 'a0000001-0000-0000-0000-000000000037',
 'Network Delay Time',
 'Find time for all nodes to receive a signal sent from a source.',
 4,
 ARRAY['{"input":"5 [[2,1,1],[2,3,1],[3,4,1]] 2","expected":"2","hidden":false}','{"input":"3 [[1,2,1]] 1","expected":"-1","hidden":true}'],
 ARRAY['5 [[2,1,1],[2,3,1],[3,4,1]] 2'],
 ARRAY['2'],
 ARRAY['def network_delay(times, n, k):'],
 1, 1, now()),

-- Concept 38: Bellman-Ford
('b0000001-0000-0000-0000-000000000075', 'a0000001-0000-0000-0000-000000000038',
 'Bellman-Ford Shortest Path',
 'Find shortest paths from source, detecting negative cycles.',
 4,
 ARRAY['{"input":"5 [[1,2,1],[2,3,1],[1,3,4]] src=1","expected":"[inf,0,1,2,inf]","hidden":false}','{"input":"3 [[1,2,-1]] src=1","expected":"[inf,0,-1]","hidden":true}'],
 ARRAY['5 [[1,2,1],[2,3,1],[1,3,4]] src=1'],
 ARRAY['[inf,0,1,2,inf]'],
 ARRAY['def bellman_ford(n, edges, src):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000076', 'a0000001-0000-0000-0000-000000000038',
 'Negative Cycle Detection',
 'Detect if a weighted directed graph contains a negative cycle.',
 4,
 ARRAY['{"input":"3 [[0,1,1],[1,2,2],[2,0,-4]]","expected":"true","hidden":false}','{"input":"3 [[0,1,1],[1,2,2]]","expected":"false","hidden":true}'],
 ARRAY['3 [[0,1,1],[1,2,2],[2,0,-4]]'],
 ARRAY['true'],
 ARRAY['def has_negative_cycle(n, edges):'],
 1, 1, now()),

-- Concept 39: Floyd-Warshall
('b0000001-0000-0000-0000-000000000077', 'a0000001-0000-0000-0000-000000000039',
 'All Pairs Shortest Path',
 'Compute shortest distances between all pairs of vertices.',
 4,
 ARRAY['{"input":"4 [[0,1,3],[1,2,1],[2,3,2]]","expected":"[[0,3,4,6],[inf,0,1,3],[inf,inf,0,2],[inf,inf,inf,0]]","hidden":false}','{"input":"3 [[0,1,2]]","expected":"[[0,2,inf],[inf,0,inf],[inf,inf,0]]","hidden":true}'],
 ARRAY['4 [[0,1,3],[1,2,1],[2,3,2]]'],
 ARRAY['[[0,3,4,6],[inf,0,1,3],[inf,inf,0,2],[inf,inf,inf,0]]'],
 ARRAY['def floyd_warshall(n, edges):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000078', 'a0000001-0000-0000-0000-000000000039',
 'Find Negative Cycle via Floyd',
 'Detect negative cycles using Floyd-Warshall algorithm.',
 4,
 ARRAY['{"input":"3 [[0,1,1],[1,2,-3],[2,0,1]]","expected":"true","hidden":false}','{"input":"3 [[0,1,1],[1,2,1]]","expected":"false","hidden":true}'],
 ARRAY['3 [[0,1,1],[1,2,-3],[2,0,1]]'],
 ARRAY['true'],
 ARRAY['def has_negative_cycle_floyd(n, edges):'],
 1, 1, now()),

-- Concept 40: Kruskal's Algorithm
('b0000001-0000-0000-0000-000000000079', 'a0000001-0000-0000-0000-000000000040',
 'Minimum Spanning Tree (Kruskal)',
 'Find MST weight using Kruskal''s algorithm.',
 4,
 ARRAY['{"input":"4 [[0,1,10],[0,2,6],[0,3,5],[1,3,15],[2,3,4]]","expected":"19","hidden":false}','{"input":"3 [[0,1,1],[1,2,2],[0,2,3]]","expected":"3","hidden":true}'],
 ARRAY['4 [[0,1,10],[0,2,6],[0,3,5],[1,3,15],[2,3,4]]'],
 ARRAY['19'],
 ARRAY['def kruskal(n, edges):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000080', 'a0000001-0000-0000-0000-000000000040',
 'MST Edge List',
 'Return the edges of the MST in sorted order.',
 4,
 ARRAY['{"input":"4 [[0,1,1],[1,2,2],[2,3,3],[0,3,5]]","expected":"[[0,1,1],[1,2,2],[2,3,3]]","hidden":false}','{"input":"3 [[0,1,1],[1,2,2]]","expected":"[[0,1,1],[1,2,2]]","hidden":true}'],
 ARRAY['4 [[0,1,1],[1,2,2],[2,3,3],[0,3,5]]'],
 ARRAY['[[0,1,1],[1,2,2],[2,3,3]]'],
 ARRAY['def mst_edges(n, edges):'],
 1, 1, now()),

-- Concept 41: Prim's Algorithm
('b0000001-0000-0000-0000-000000000081', 'a0000001-0000-0000-0000-000000000041',
 'Minimum Spanning Tree (Prim)',
 'Find MST weight using Prim''s algorithm from vertex 0.',
 4,
 ARRAY['{"input":"5 [[0,1,2],[0,3,6],[1,2,3],[1,3,8],[1,4,5],[2,4,7],[3,4,9]]","expected":"16","hidden":false}','{"input":"4 [[0,1,1],[0,2,4],[1,2,2],[2,3,3]]","expected":"6","hidden":true}'],
 ARRAY['5 [[0,1,2],[0,3,6],[1,2,3],[1,3,8],[1,4,5],[2,4,7],[3,4,9]]'],
 ARRAY['16'],
 ARRAY['def prim(n, edges):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000082', 'a0000001-0000-0000-0000-000000000041',
 'Prim with Priority Queue',
 'Implement Prim''s using a min-heap priority queue.',
 4,
 ARRAY['{"input":"3 [[0,1,1],[1,2,2],[0,2,4]]","expected":"3","hidden":false}','{"input":"4 [[0,1,3],[0,2,1],[2,1,1],[2,3,4]]","expected":"6","hidden":true}'],
 ARRAY['3 [[0,1,1],[1,2,2],[0,2,4]]'],
 ARRAY['3'],
 ARRAY['def prim_heap(n, edges):'],
 1, 1, now()),

-- Concept 42: Weighted Graphs
('b0000001-0000-0000-0000-000000000083', 'a0000001-0000-0000-0000-000000000042',
 'Weighted Graph Shortest Path',
 'Find shortest path in a weighted graph using appropriate algorithm.',
 3,
 ARRAY['{"input":"4 [[0,1,2],[1,2,1],[2,3,3]] start=0 end=3","expected":"6","hidden":false}','{"input":"3 [[0,1,5],[1,2,1]] start=0 end=2","expected":"6","hidden":true}'],
 ARRAY['4 [[0,1,2],[1,2,1],[2,3,3]] start=0 end=3'],
 ARRAY['6'],
 ARRAY['def weighted_shortest(n, edges, start, end):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000084', 'a0000001-0000-0000-0000-000000000042',
 'Cheapest Flights Within K Stops',
 'Find cheapest price from src to dst with at most k stops.',
 3,
 ARRAY['{"input":"3 [[0,1,100],[1,2,100],[0,2,500]] 0 2 1","expected":"200","hidden":false}','{"input":"3 [[0,1,100],[1,2,100],[0,2,500]] 0 2 0","expected":"500","hidden":true}'],
 ARRAY['3 [[0,1,100],[1,2,100],[0,2,500]] 0 2 1'],
 ARRAY['200'],
 ARRAY['def find_cheapest(n, flights, src, dst, k):'],
 1, 1, now()),

-- Concept 43: Shortest Path Algorithms
('b0000001-0000-0000-0000-000000000085', 'a0000001-0000-0000-0000-000000000043',
 'Path with Maximum Probability',
 'Find path with maximum probability (product of edge values).',
 3,
 ARRAY['{"input":"3 [[0,1,0.5],[1,2,0.5],[0,2,0.2]] 0 2","expected":"0.25","hidden":false}','{"input":"3 [[0,1,0.5],[1,2,0.5]] 0 2","expected":"0.25","hidden":true}'],
 ARRAY['3 [[0,1,0.5],[1,2,0.5],[0,2,0.2]] 0 2'],
 ARRAY['0.25'],
 ARRAY['def max_probability(n, edges, src, dst):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000086', 'a0000001-0000-0000-0000-000000000043',
 'Cheapest Path with Exact Stops',
 'Find cheapest path with exactly k edges.',
 4,
 ARRAY['{"input":"4 [[0,1,100],[1,2,100],[2,0,100],[1,3,600],[2,3,200]] 0 3 2","expected":"700","hidden":false}','{"input":"3 [[0,1,100],[1,2,100]] 0 2 1","expected":"-1","hidden":true}'],
 ARRAY['4 [[0,1,100],[1,2,100],[2,0,100],[1,3,600],[2,3,200]] 0 3 2'],
 ARRAY['700'],
 ARRAY['def cheapest_with_stops(n, edges, src, dst, k):'],
 1, 1, now()),

-- Concept 44: Minimum Spanning Tree
('b0000001-0000-0000-0000-000000000087', 'a0000001-0000-0000-0000-000000000044',
 'Connect All Cities',
 'Find minimum cost to connect all cities (MST).',
 4,
 ARRAY['{"input":"3 [[0,1,1],[1,2,2],[0,2,3]]","expected":"3","hidden":false}','{"input":"4 [[0,1,1],[1,2,2],[2,3,3],[0,3,6]]","expected":"6","hidden":true}'],
 ARRAY['3 [[0,1,1],[1,2,2],[0,2,3]]'],
 ARRAY['3'],
 ARRAY['def min_cost_connect(n, edges):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000088', 'a0000001-0000-0000-0000-000000000044',
 'Critical Connections',
 'Find all critical (bridges) connections in a network.',
 4,
 ARRAY['{"input":"4 [[0,1],[1,2],[2,0],[1,3]]","expected":"[[1,3]]","hidden":false}','{"input":"3 [[0,1],[1,2]]","expected":"[[0,1],[1,2]]","hidden":true}'],
 ARRAY['4 [[0,1],[1,2],[2,0],[1,3]]'],
 ARRAY['[[1,3]]'],
 ARRAY['def critical_connections(n, connections):'],
 1, 1, now()),

-- ======================================================================
-- Algorithms: Backtracking (45)
-- ======================================================================

('b0000001-0000-0000-0000-000000000089', 'a0000001-0000-0000-0000-000000000045',
 'N-Queens',
 'Place n queens on an n×n chessboard so no two queens attack each other.',
 4,
 ARRAY['{"input":"4","expected":"2","hidden":false}','{"input":"1","expected":"1","hidden":true}'],
 ARRAY['4'],
 ARRAY['2'],
 ARRAY['def n_queens(n):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000090', 'a0000001-0000-0000-0000-000000000045',
 'Sudoku Solver',
 'Solve a Sudoku puzzle by filling empty cells (represented as 0).',
 4,
 ARRAY['{"input":"[[5,3,0,0,7,0,0,0,0],[6,0,0,1,9,5,0,0,0],[0,9,8,0,0,0,0,6,0],[8,0,0,0,6,0,0,0,3],[4,0,0,8,0,3,0,0,1],[7,0,0,0,2,0,0,0,6],[0,6,0,0,0,0,2,8,0],[0,0,0,4,1,9,0,0,5],[0,0,0,0,8,0,0,7,9]]","expected":"solved","hidden":false}','{"input":"[[0,0,0],[0,0,0],[0,0,0]]","expected":"solved","hidden":true}'],
 ARRAY['[[5,3,0,0,7,0,0,0,0],[6,0,0,1,9,5,0,0,0],[0,9,8,0,0,0,0,6,0],[8,0,0,0,6,0,0,0,3],[4,0,0,8,0,3,0,0,1],[7,0,0,0,2,0,0,0,6],[0,6,0,0,0,0,2,8,0],[0,0,0,4,1,9,0,0,5],[0,0,0,0,8,0,0,7,9]]'],
 ARRAY['solved'],
 ARRAY['def solve_sudoku(board):'],
 1, 1, now()),

-- ======================================================================
-- Dynamic Programming (46-49)
-- ======================================================================

-- Concept 46: Memoization
('b0000001-0000-0000-0000-000000000091', 'a0000001-0000-0000-0000-000000000046',
 'Climbing Stairs Memoized',
 'Count distinct ways to climb n stairs (1 or 2 steps at a time) using memoization.',
 2,
 ARRAY['{"input":"5","expected":"8","hidden":false}','{"input":"1","expected":"1","hidden":true}'],
 ARRAY['5'],
 ARRAY['8'],
 ARRAY['def climb_stairs(n, memo={}):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000092', 'a0000001-0000-0000-0000-000000000046',
 'House Robber Memoized',
 'Rob houses for maximum money without robbing adjacent houses.',
 2,
 ARRAY['{"input":"[2,7,9,3,1]","expected":"12","hidden":false}','{"input":"[1,2,3,1]","expected":"4","hidden":true}'],
 ARRAY['[2,7,9,3,1]'],
 ARRAY['12'],
 ARRAY['def rob(nums, memo={}):'],
 1, 1, now()),

-- Concept 47: Tabulation
('b0000001-0000-0000-0000-000000000093', 'a0000001-0000-0000-0000-000000000047',
 'Climbing Stairs Tabulated',
 'Count distinct ways to climb n stairs using bottom-up tabulation.',
 2,
 ARRAY['{"input":"10","expected":"89","hidden":false}','{"input":"2","expected":"2","hidden":true}'],
 ARRAY['10'],
 ARRAY['89'],
 ARRAY['def climb_stairs(n):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000094', 'a0000001-0000-0000-0000-000000000047',
 'Minimum Path Sum',
 'Find the path from top-left to bottom-right with minimum sum in a grid.',
 3,
 ARRAY['{"input":"[[1,3,1],[1,5,1],[4,2,1]]","expected":"7","hidden":false}','{"input":"[[1,2,3],[4,5,6]]","expected":"12","hidden":true}'],
 ARRAY['[[1,3,1],[1,5,1],[4,2,1]]'],
 ARRAY['7'],
 ARRAY['def min_path_sum(grid):'],
 1, 1, now()),

-- Concept 48: Knapsack Problem
('b0000001-0000-0000-0000-000000000095', 'a0000001-0000-0000-0000-000000000048',
 '0/1 Knapsack',
 'Given weights and values, find maximum value with capacity W.',
 3,
 ARRAY['{"input":"weights=[1,2,3] values=[6,10,12] capacity=5","expected":"22","hidden":false}','{"input":"weights=[1,1] values=[1,1] capacity=2","expected":"2","hidden":true}'],
 ARRAY['weights=[1,2,3] values=[6,10,12] capacity=5'],
 ARRAY['22'],
 ARRAY['def knapsack(weights, values, capacity):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000096', 'a0000001-0000-0000-0000-000000000048',
 'Coin Change',
 'Find minimum number of coins to make a given amount.',
 3,
 ARRAY['{"input":"coins=[1,5,10,25] amount=30","expected":"2","hidden":false}','{"input":"coins=[2] amount=3","expected":"-1","hidden":true}'],
 ARRAY['coins=[1,5,10,25] amount=30'],
 ARRAY['2'],
 ARRAY['def coin_change(coins, amount):'],
 1, 1, now()),

-- Concept 49: Longest Common Subsequence
('b0000001-0000-0000-0000-000000000097', 'a0000001-0000-0000-0000-000000000049',
 'Longest Common Subsequence',
 'Find the length of the longest common subsequence of two strings.',
 3,
 ARRAY['{"input":"abcde ace","expected":"3","hidden":false}','{"input":"abc abc","expected":"3","hidden":true}'],
 ARRAY['abcde ace'],
 ARRAY['3'],
 ARRAY['def lcs(s1, s2):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000098', 'a0000001-0000-0000-0000-000000000049',
 'Edit Distance',
 'Find minimum operations (insert, delete, replace) to convert word1 to word2.',
 3,
 ARRAY['{"input":"horse ros","expected":"3","hidden":false}','{"input":"intention execution","expected":"5","hidden":true}'],
 ARRAY['horse ros'],
 ARRAY['3'],
 ARRAY['def edit_distance(word1, word2):'],
 1, 1, now()),

-- ======================================================================
-- Algorithms: Greedy (50)
-- ======================================================================

('b0000001-0000-0000-0000-000000000099', 'a0000001-0000-0000-0000-000000000050',
 'Activity Selection',
 'Given start and end times, select maximum non-overlapping activities.',
 3,
 ARRAY['{"input":"[[1,4],[3,5],[0,6],[5,7],[3,9],[5,9],[6,10],[8,11]]","expected":"3","hidden":false}','{"input":"[[1,2],[2,3],[3,4]]","expected":"3","hidden":true}'],
 ARRAY['[[1,4],[3,5],[0,6],[5,7],[3,9],[5,9],[6,10],[8,11]]'],
 ARRAY['3'],
 ARRAY['def activity_selection(activities):'],
 1, 1, now()),

('b0000001-0000-0000-0000-000000000100', 'a0000001-0000-0000-0000-000000000050',
 'Jump Game',
 'Determine if you can reach the last index of an array (each element = max jump length).',
 2,
 ARRAY['{"input":"[2,3,1,1,4]","expected":"true","hidden":false}','{"input":"[3,2,1,0,4]","expected":"false","hidden":true}'],
 ARRAY['[2,3,1,1,4]'],
 ARRAY['true'],
 ARRAY['def can_jump(nums):'],
 1, 1, now())

ON CONFLICT ("Id") DO NOTHING;

-- ======================================================================
-- Verification Queries (run after seeding)
-- ======================================================================

-- Total seeded question count (should be 100; scoped to seed IDs, excludes pre-existing rows)
-- SELECT COUNT(*) AS total_questions FROM "Questions" WHERE "Id"::text LIKE 'b0000001-%';

-- Seeded questions per concept (should be 2 each)
-- SELECT "ConceptId", COUNT(*) AS cnt FROM "Questions" WHERE "Id"::text LIKE 'b0000001-%'
-- GROUP BY "ConceptId" HAVING COUNT(*) < 2;

-- Seeded questions with a hidden test case (each seed row has exactly one hidden case)
-- SELECT COUNT(*) AS questions_with_hidden FROM "Questions" q
-- WHERE q."Id"::text LIKE 'b0000001-%'
-- AND EXISTS (SELECT 1 FROM unnest(q."TestCases") AS tc WHERE tc::jsonb ->> 'hidden' = 'true');

-- All 50 seeded concepts should have questions
-- SELECT c."Id", c."Name", COUNT(q."Id") AS question_count
-- FROM "Concepts" c LEFT JOIN "Questions" q ON q."ConceptId" = c."Id"
-- WHERE c."Id"::text LIKE 'a0000001-%'
-- GROUP BY c."Id", c."Name" ORDER BY c."Id";

COMMIT;
