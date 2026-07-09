# Fixing the "Willa problem": onboarding people who aren't on the tree yet

Most family tree software assumes you already know where you fit before you show up. I wanted the opposite: someone should be able to email a distant, half-remembered relative — "hey, write down what you remember about Uncle Bill" — and have that person land in a working app even though neither they nor Bill exist in the system yet.

That's a genuinely awkward onboarding problem. The invited person (call her Willa) has no account, no tree position, and the person she's writing about (Bill) has no `Person` row either. By the time she finishes her story and creates an account, the app needs to answer two questions in the right order: *who is Bill, relative to the existing tree?* and only then, *who is Willa, relative to Bill?*

I built a guided linking modal with a small state machine that asks about the story's subject first — "Is Bill already on this tree?", and if not, "Is Bill related to anyone on it?" — before ever asking about the new user herself. Getting the question order right took several iterations; the first version asked generic "do you know anyone on this tree?" questions that made no sense once you realized the whole point of the invite was a specific person, not a generic connection.

## The bug

The interesting bug showed up after the dialog flow was working end-to-end: Bill and Willa rendered as a single overlapping circle, completely disconnected from the rest of the tree.

Tracing it back, the layout engine's core algorithms — connected-component detection, generational depth calculation, birth-year inference — all walked `ParentIds`/`ChildIds`/`SpouseIds`, but never `SiblingIds`. An explicit sibling relationship with no shared parent (exactly the shape you get when you add someone's previously-unknown brother) was invisible to every layout pass. Two people with no inferable birth year both fell through to the same default (the tree's median year), landed at the same X (since a single child centers directly under its one parent), and rendered as one node.

## The fix

I threaded sibling-awareness through three cooperating passes — component membership, generational depth, and birth-year inference — as a single converging loop, since a sibling picking up a depth or year can unlock a further inference for *their* children in a later round. I also added a lightweight dashed connector type for sibling links that have no parent/couple group to hang a normal connector off of, plus a root-placement reorder step so sibling-linked people land next to each other instead of wherever iteration order happened to put them.

## Side lesson

Registering a new account in Blazor Server doesn't authenticate the browser, because a SignalR circuit can't set an HTTP-only auth cookie. The fix was routing the post-registration flow through a real hidden-form POST to the same login endpoint the visible login form uses — a good reminder that "it looks like one request" in a SPA-like framework can hide a hard boundary underneath.
