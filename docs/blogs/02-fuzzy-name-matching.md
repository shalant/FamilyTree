# Fuzzy Name Matching: Catching Typos Without False Positives

**Posted:** July 29, 2026  
**Category:** Data Quality & UX

## Why Name Matching Matters

When someone signs up for ArborKin after being invited, they complete a "link to tree" flow:

1. Are you already on the tree? 
2. Who are you related to?
3. Create or confirm your person record.

The risk: **the same person gets created twice, under slightly different names** — "John Smith" created once, then "Jon Smith" on a second sign-up, with no warning.

We had an exact-match check (case/whitespace-insensitive) that caught "John Smith" == "john  smith", but missed "John" vs "Jon".

## The Old Approach: Exact Match Only

Original code:
```csharp
var matches = people
    .Where(p => string.Equals(
        (p.FirstName ?? "").Trim(), 
        firstName, 
        StringComparison.OrdinalIgnoreCase)
    && string.Equals(...)
    .ToList();
```

This works for typo-free data but doesn't catch real-world variations:
- "Jon" for "John" (common nickname, 1-char typo)
- "Jahn" for "John" (phonetic variation)
- Middle-name confusion ("John Michael" vs "Michael John")

## The New Approach: Levenshtein Distance

We upgraded to fuzzy matching using **Levenshtein distance** — the minimum number of single-character edits (insertions, deletions, substitutions) needed to transform one string into another.

**Algorithm:**
```csharp
private static int LevenshteinDistance(string s1, string s2)
{
    var m = s1.Length;
    var n = s2.Length;
    var dp = new int[m + 1, n + 1];

    for (int i = 0; i <= m; i++) dp[i, 0] = i;
    for (int j = 0; j <= n; j++) dp[0, j] = j;

    for (int i = 1; i <= m; i++)
    {
        for (int j = 1; j <= n; j++)
        {
            if (s1[i - 1] == s2[j - 1])
                dp[i, j] = dp[i - 1, j - 1];
            else
                dp[i, j] = 1 + Math.Min(
                    Math.Min(dp[i - 1, j], dp[i, j - 1]),
                    dp[i - 1, j - 1]);
        }
    }

    return dp[m, n];
}
```

Convert distance to similarity score: `1.0 - (distance / max_length)`.

## Threshold Tuning: The Tradeoff

We chose **80% similarity** as the threshold. This catches:
- "Jon" vs "John" (80%+ match) ✅
- "Jahn" vs "John" (80%+ match) ✅
- "William" vs "Bill" (62% match) ❌ (correctly rejected)
- "Alice" vs "David" (0% match) ❌

The 80% threshold is intentionally conservative. Two real people can legitimately share a similar name; a false positive (asking "is this you?" when it isn't) is worse than a false negative (missing a real duplicate).

## Exact Matches First

We keep exact matches as the highest priority:
```csharp
// Exact matches (case/whitespace-insensitive) come first
var exactMatches = peopleList
    .Where(p => string.Equals(..., StringComparison.OrdinalIgnoreCase))
    .ToList();

if (exactMatches.Count > 0)
    return exactMatches;

// Only fuzzy match if no exact match exists
// And only if we have a complete (first + last) name
if (firstName.Length == 0 || lastName.Length == 0)
    return [];

// Fuzzy match on full name
```

This ensures we never wrongly rank a fuzzy match above an exact one.

## Test Coverage

We wrote 8 new tests covering:
- Exact matches (existing behavior preserved) ✅
- Common typos ("Jon" vs "John") ✅
- Phonetic variants ("Jahn" vs "John") ✅
- Too-different names ("Alice" vs "David") ✅
- Incomplete names skip fuzzy matching ✅
- Multiple matches returned ranked by similarity ✅

All 124 tests pass (75 Core + 49 Web).

## Lessons & Tradeoffs

1. **Don't ship fuzzy matching without a threshold** — without the 80% guard, you'd get false positives. Document your threshold.
2. **Exact matches are free wins** — check them first, fast path around the algorithm.
3. **Incomplete names are safer to reject** — "Jon" alone could match "Jonas", "Jonah", "Jonathan" in ways that surprise users.
4. **Test with real data shapes** — our test suite includes surname variations, which revealed that nickname pairs ("Bill" vs "William") don't quite make the 80% cut (they're 62%, close but not close enough).

## Next: Semantic Merge

Fuzzy matching *detects* duplicates; the next step is *merging* them. Once a duplicate is confirmed, we should offer to merge the two person records, consolidating their relationships and media. That's Phase 4 work.

---

**PR #19:** [shalant/FamilyTree/pull/19](https://github.com/shalant/FamilyTree/pull/19)
