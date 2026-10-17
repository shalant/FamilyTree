using FamilyTree.Core.Mappers;
using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FamilyTree.Web.Services;
using FluentAssertions;
using Xunit;
using static FamilyTree.Web.Services.FamilyTreeLayoutEngine;

namespace FamilyTree.Web.Tests.ServiceTests;

// Classification-transition before/after pairs (Leg 2 of
// docs/Testing/fuzz-and-adversarial-review-plan.md, added 2026-10-11).
//
// Every layout bug found so far came from an edit that moved a person into a different
// structural bucket (CLAUDE.md, "Where Layout Bugs Hide: Classification Transitions").
// Each test here lays out the same family twice, differing by exactly one edit a user can
// make in the app, and checks the "after" picture for the general invariants plus what
// that specific edit must not disturb.
//
// Families are built as Person + Relationship rows and go through PersonMapper and
// CoupleHelper, the same path production uses, so the DTO lists are always symmetric.
//
// Every test runs twice, with ascending and descending GUIDs. Spouse and Sibling rows are
// stored lower-GUID-first and CoupleHelper keys couples the same way, so which partner is
// "A" (and lands on the left) depends on GUID order. A result that only holds for one order
// is GUID luck, the shape of the flaky 2026-07-06 bug (CLAUDE.md, Root-Group Placement Order).
public class FamilyTreeLayoutTransitionTests
{
    // Mirror of the private constant in FamilyTreeLayoutEngine.
    private const int SpouseSpacingX = 200;
    private const int NodeSpacingX = 120;

    private const string KnownOpen = "Known open, see docs/TodoList.md ";

    // ─────────────────────────────────────────────────────────────────────────
    // MARRIAGES
    // ─────────────────────────────────────────────────────────────────────────

    // A marriage with children ends (divorce, or a death recorded as the end date).
    // Mapper moves the partner from SpouseIds to FormerSpouseIds, but the shared children
    // still make them a couple, so nothing should move at all: only the connector style
    // changes (solid to dashed).
    [Theory, InlineData(false), InlineData(true)]
    public void MarriageWithChildrenEnds_NobodyMoves(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gpa = t.Add("Gus", "Small", 1870);
        var louis = t.Add("Louis", "Small", 1898);
        var lillian = t.Add("Lillian", "Small", 1900);
        var ray = t.Add("Ray", "Small", 1922);
        var sis = t.Add("Sis", "Small", 1924);
        t.Parent(gpa, louis).Marry(louis, lillian).Parent(louis, ray).Parent(lillian, ray)
         .Parent(louis, sis).Parent(lillian, sis);

        var before = t.Layout(ray);
        var after = t.Clone().EndMarriage(louis, lillian, new DateOnly(1922, 3, 1)).Layout(ray);

        AssertSound(t, after);
        AssertSamePositions(before, after, t.Ids);
    }

    // A widower with a child from the first (ended) marriage remarries and has a child
    // with the second wife. Before: the second marriage is childless. After: both of the
    // person's couples have children, so primaryGroup's "prefer the group with children"
    // rule (2026-07-07, Florence/Harvey) no longer decides it and couple sort order does.
    // The Louis/Lillian/Libby shape from commit 073ae02.
    //
    // Found 2026-10-11 (Leg 2): Louis's primary group flips to the Libby marriage, because
    // "Libby" sorts before "Lillian". Lillian then goes through the spouse safety net and
    // Ray is appended at the right edge instead of under his parents. The "before" state
    // already overlaps (Libby lands on Lillian: R4, R7). Fixed 2026-10-12 (R11): the
    // marriage with the oldest child stays primary, and the second marriage is placed on
    // Louis's free side with its slot reserved even while it's childless.
    [Theory, InlineData(false), InlineData(true)]
    public void ChildlessSecondMarriageGainsAChild_FirstMarriagesChildStaysUnderItsParents(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var louis = t.Add("Louis", "Small", 1898);
        var lillian = t.Add("Lillian", "Small", 1900);
        var libby = t.Add("Libby", "Small", 1902);
        var ray = t.Add("Ray", "Small", 1922);
        t.Marry(louis, lillian, ended: new DateOnly(1922, 3, 1)).Parent(louis, ray).Parent(lillian, ray)
         .Marry(louis, libby);

        var before = t.Layout(ray);
        var after = t.Clone();
        var sid = after.Add("Sid", "Small", 1928);
        after.Parent(louis, sid).Parent(libby, sid);
        var afterLayout = after.Layout(ray);

        AssertSamePositions(before, afterLayout, [louis, lillian, ray], "Ray's own family must not be rearranged by a half-brother arriving");
        AssertSound(t, before);
        AssertSound(after, afterLayout);
        AssertChildUnderParents(after, afterLayout, ray);
        AssertChildUnderParents(after, afterLayout, sid);
    }

    // Same shape, but both marriages have several children, so the second marriage's
    // children can't sit at the second couple's midpoint without running into the first
    // marriage's children and are pushed outward. A third child arriving in the second
    // marriage may move the first family, but only as a block (the second marriage needs
    // more room beside it), never by reshuffling it.
    [Theory, InlineData(false), InlineData(true)]
    public void SecondMarriageWithChildrenGainsAnother_FirstFamilyMovesOnlyAsABlock(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var louis = t.Add("Louis", "Small", 1898);
        var lillian = t.Add("Lillian", "Small", 1900);
        var libby = t.Add("Libby", "Small", 1902);
        var ray = t.Add("Ray", "Small", 1920);
        var sis = t.Add("Sis", "Small", 1921);
        var bo = t.Add("Bo", "Small", 1922);
        var sid = t.Add("Sid", "Small", 1928);
        var sue = t.Add("Sue", "Small", 1930);
        t.Marry(louis, lillian, ended: new DateOnly(1923, 3, 1)).Marry(louis, libby);
        foreach (var c in new[] { ray, sis, bo }) t.Parent(louis, c).Parent(lillian, c);
        foreach (var c in new[] { sid, sue }) t.Parent(louis, c).Parent(libby, c);

        var before = t.Layout(ray);
        var after = t.Clone();
        var sam = after.Add("Sam", "Small", 1932);
        after.Parent(louis, sam).Parent(libby, sam);
        var afterLayout = after.Layout(ray);

        AssertSound(t, before);
        AssertSound(after, afterLayout);
        AssertSameRelativePlacement(before, afterLayout, [louis, lillian, ray, sis, bo],
            "the first family keeps its shape when the second marriage grows");
        foreach (var c in new[] { ray, sis, bo, sid, sue, sam })
            AssertChildUnderParents(after, afterLayout, c);
    }

    // Two single parents marry (R2's first repro): Rose has Bud, Ellen has Ellen Jr, no
    // shared children. Each one's own single-parent group is their primary, so the
    // childless marriage is placed by the cross-root step, which used to keep the two
    // families OrphanBufferX apart (partners 360px apart). Fixed 2026-10-12 with R6.
    [Theory, InlineData(false), InlineData(true)]
    public void TwoSingleParentsMarry_PartnersSitTogetherAboveTheirOwnChildren(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var rose = t.Add("Rose", "Kuh", 1940);
        var bud = t.Add("Bud", "Kuh", 1965);
        var ellen = t.Add("Ellen", "Small", 1942);
        var ellenJr = t.Add("Ellen Jr", "Small", 1968);
        t.Parent(rose, bud).Parent(ellen, ellenJr);

        var after = t.Clone().Marry(rose, ellen);
        var afterLayout = after.Layout(rose);

        AssertSound(after, afterLayout);
        AssertChildUnderParents(after, afterLayout, bud);
        AssertChildUnderParents(after, afterLayout, ellenJr);
    }

    // A person already in two marriages (first ended, with a child; second ended, with a
    // child) marries a third time, childless. The new spouse has no group of their own and
    // goes through the safety-net spouse anchor.
    //
    // Found 2026-10-11 (Leg 2): the "before" state is already wrong. Pat's second partner
    // Blair goes through the safety net and lands right of Pat even when Alex holds that
    // side (R7), and Casey then lands on top of Blair because the collision check only
    // looks at nodes on exactly Pat's Y (R4). Blair has been placed on Pat's free side
    // since R11 (2026-10-12); the overlap was fixed 2026-10-15 by checking collisions at
    // the placed person's own row.
    [Theory, InlineData(false), InlineData(true)]
    public void ThirdMarriage_NewSpouseLandsBesidePartnerAndNobodyElseMoves(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var pat = t.Add("Pat", "Kuh", 1950);
        var alex = t.Add("Alex", "Kuh", 1950);
        var blair = t.Add("Blair", "Kuh", 1952);
        var k1 = t.Add("Kim", "Kuh", 1975);
        var k2 = t.Add("Kit", "Kuh", 1982);
        t.Marry(pat, alex, ended: new DateOnly(1978, 1, 1)).Parent(pat, k1).Parent(alex, k1)
         .Marry(pat, blair, ended: new DateOnly(1990, 1, 1)).Parent(pat, k2).Parent(blair, k2);

        var before = t.Layout(pat);
        var after = t.Clone();
        var casey = after.Add("Casey", "Kuh", 1955);
        after.Marry(pat, casey);
        var afterLayout = after.Layout(pat);

        AssertSound(after, afterLayout, maxCouplesPerPerson: 2);
        AssertSamePositions(before, afterLayout, t.Ids, "a childless third marriage must not rearrange the existing family");
        var nodes = ById(afterLayout);
        Math.Abs(nodes[casey].X - nodes[pat].X).Should().BeLessThanOrEqualTo(2 * SpouseSpacingX,
            "Casey must land beside Pat, not at the far edge of the tree");
    }

    // R7's first repro: Bud is married to Bud Lee (childless) and then also to Harvey
    // (childless, no other relatives, no end date recorded). Harvey has no group of his
    // own and goes through the spouse safety net, which always tried the right side
    // first and stepped past whoever held it, so Harvey landed past Bud Lee, 360px from
    // Bud, with Bud's left side free.
    [Theory, InlineData(false), InlineData(true)]
    public void SecondChildlessMarriage_NewSpouseTakesTheFreeSideOfTheirPartner(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gus = t.Add("Gus", "Kuh", 1920);
        var bud = t.Add("Bud", "Kuh", 1950);
        var budLee = t.Add("Bud Lee", "Kuh", 1951);
        t.Parent(gus, bud).Marry(bud, budLee);

        var before = t.Layout(bud);
        var after = t.Clone();
        var harvey = after.Add("Harvey", "Kuh", 1949);
        after.Marry(bud, harvey);
        var afterLayout = after.Layout(bud);

        AssertSound(t, before);
        AssertSound(after, afterLayout);
        // Harvey's slot is reserved beside the group (R11), so the couple may shift as a block.
        AssertSameRelativePlacement(before, afterLayout, [bud, budLee], "a second childless marriage must not rearrange the first");
    }

    // R7's second repro: Ann (sibling Bill beside her) has a child alone, then marries
    // Rose (no other relatives). Rose landed 240px from Ann because the slot to Ann's
    // right was taken.
    [Theory, InlineData(false), InlineData(true)]
    public void SingleParentWithASiblingMarries_SpouseSitsBesideThem(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gus = t.Add("Gus", "Kuh", 1920);
        var ann = t.Add("Ann", "Kuh", 1950);
        var bill = t.Add("Bill", "Kuh", 1952);
        var kit = t.Add("Kit", "Kuh", 1975);
        t.Parent(gus, ann).Parent(gus, bill).Parent(ann, kit);

        var before = t.Layout(ann);
        var after = t.Clone();
        var rose = after.Add("Rose", "Small", 1951);
        after.Marry(ann, rose);
        var afterLayout = after.Layout(ann);

        AssertSound(t, before);
        AssertSound(after, afterLayout);
        AssertChildUnderParents(after, afterLayout, kit);
    }

    // R7 through the spouse safety net: Harvey has no group of his own (two childless
    // marriages, neither his primary) and is anchored beside the rightmost of his wives.
    // The search always tried the right side first and stepped past whoever held it.
    //
    // Not fixed by the 2026-10-15 nearer-side change: Harvey was actually Dora's R11
    // attachment, with Florence's family on the far side of Max+Dora's. Fixed the same day
    // by counting Harvey as part of Dora's family in the cross-root check, so his marriage
    // to Florence brings the two families together, facing each other.
    [Theory, InlineData(false), InlineData(true)]
    public void PersonMarriedIntoTwoFamilies_SitsBesideAWife(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var bud = t.Add("Bud", "Rosenberg", 1900);
        var florence = t.Add("Florence", "Kuh", 1902);
        var marc = t.Add("Marc", "Rosenberg", 1925);
        var max = t.Add("Max", "Lee", 1898);
        var dora = t.Add("Dora", "Lee", 1903);
        var gish = t.Add("Gish", "Lee", 1928);
        t.Marry(bud, florence, ended: new DateOnly(1930, 1, 1)).Parent(bud, marc).Parent(florence, marc)
         .Marry(max, dora, ended: new DateOnly(1932, 1, 1)).Parent(max, gish).Parent(dora, gish);

        var after = t.Clone();
        var harvey = after.Add("Harvey", "Fleishman", 1901);
        after.Marry(harvey, florence, ended: new DateOnly(1935, 1, 1)).Marry(harvey, dora);
        var layout = after.Layout(marc);

        AssertSound(after, layout);
    }

    // R10: Al (Gus's son) marries Bet (Bo's daughter), a cross-root couple, and Bet's
    // brother Bud has three children of his own beside her. A cross-root couple's children
    // were dropped centred on the couple's midpoint with no collision check, so Al and
    // Bet's fourth child arriving spread their row into Bud's children's slots.
    [Theory, InlineData(false), InlineData(true)]
    public void CrossRootCouplesChildren_DoNotLandOnTheirCousins(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gus = t.Add("Gus", "Kuh", 1900);
        var gail = t.Add("Gail", "Kuh", 1902);
        var al = t.Add("Al", "Kuh", 1925);
        var bo = t.Add("Bo", "Lee", 1901);
        var bea = t.Add("Bea", "Lee", 1903);
        var bet = t.Add("Bet", "Lee", 1926);
        var bud = t.Add("Bud", "Lee", 1927);
        var dot = t.Add("Dot", "Lee", 1928);
        t.Marry(gus, gail).Parent(gus, al).Parent(gail, al)
         .Marry(bo, bea).Parent(bo, bet).Parent(bea, bet).Parent(bo, bud).Parent(bea, bud)
         .Marry(bud, dot).Marry(al, bet);
        foreach (var (name, year) in new[] { ("Dee", 1950), ("Dan", 1951), ("Dru", 1952) })
        {
            var c = t.Add(name, "Lee", year);
            t.Parent(bud, c).Parent(dot, c);
        }
        foreach (var (name, year) in new[] { ("Kim", 1950), ("Kit", 1951), ("Kay", 1952) })
        {
            var c = t.Add(name, "Kuh", year);
            t.Parent(al, c).Parent(bet, c);
        }

        var after = t.Clone();
        var kip = after.Add("Kip", "Kuh", 1953);
        after.Parent(al, kip).Parent(bet, kip);

        AssertSound(t, t.Layout(gus));
        var afterLayout = after.Layout(gus);
        AssertSound(after, afterLayout);
        foreach (var c in after.People().Where(p => p.ParentIds?.Contains(al) == true))
            AssertChildUnderParents(after, afterLayout, c.Id);
    }

    // An orphan sibling (no parents on the tree) is linked to Ray, who has his wife on one
    // side and nobody on the other. The anchor search tried the right side first, so when
    // Rose held it Morton was stepped past her instead of taking the free slot by Ray.
    [Theory, InlineData(false), InlineData(true)]
    public void OrphanSiblingOfAMarriedMan_LandsOnHisFreeSide(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var ray = t.Add("Ray", "Small", 1910);
        var rose = t.Add("Rose", "Small", 1912);
        var ellen = t.Add("Ellen", "Small", 1935);
        t.Marry(ray, rose).Parent(ray, ellen).Parent(rose, ellen);

        var after = t.Clone();
        var morton = after.Add("Morton", "Small", 1908);
        after.Sibling(ray, morton);
        var layout = after.Layout(ray);

        AssertSound(after, layout);
        var nodes = ById(layout);
        Math.Abs(nodes[morton].X - nodes[ray].X).Should().BeLessThanOrEqualTo(NodeSpacingX,
            "Morton must take the free slot beside Ray, not be stepped past Rose");
    }

    // Plan item: "a second marriage ending (EndDate set) while a first is still active."
    // Pat is married to Alex (child Kim) and, with no end date recorded, also to Blair
    // (childless, no other relatives): the Florence/Harvey shape. Recording the Blair
    // marriage as ended moves Blair to FormerSpouseIds, and Blair's only link is now a
    // former-spouse link.
    [Theory, InlineData(false), InlineData(true)]
    public void ChildlessSecondMarriageEnds_FormerSpouseStaysBesidePartner(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var pat = t.Add("Pat", "Kuh", 1950);
        var alex = t.Add("Alex", "Kuh", 1950);
        var blair = t.Add("Blair", "Lee", 1952);
        var kim = t.Add("Kim", "Kuh", 1975);
        t.Marry(pat, alex).Parent(pat, kim).Parent(alex, kim).Marry(pat, blair);

        var before = t.Layout(pat);
        var after = t.Clone().EndMarriage(pat, blair, new DateOnly(1980, 1, 1)).Layout(pat);

        AssertSound(t, after);
        AssertSamePositions(before, after, [pat, alex, kim]);
        var nodes = ById(after);
        Math.Abs(nodes[blair].X - nodes[pat].X).Should().BeLessThanOrEqualTo(2 * SpouseSpacingX,
            "ending the marriage must not send Blair to a separate tree beside the family");
    }

    // R1 repro from docs/TodoList.md: Ann is Willa's parent, Willa married Max, nobody has a
    // date. Ending the marriage leaves Max linked only as a former spouse. The engine used to
    // treat him as a separate tree, seed his year at the current year, and pass that on to
    // Willa through spouse inference, which put Willa on Ann's row.
    [Theory, InlineData(false), InlineData(true)]
    public void DatelessMarriageEnds_ExSpouseStaysOnPartnersRowAndChildStaysBelowParent(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var ann = t.Add("Ann", "Ash");
        var willa = t.Add("Willa", "Ash");
        var max = t.Add("Max", "Moe");
        t.Parent(ann, willa).Marry(willa, max);

        var before = t.Layout(willa);
        var after = t.Clone().EndMarriage(willa, max, new DateOnly(1990, 1, 1)).Layout(willa);

        AssertSound(t, after);
        AssertSamePositions(before, after, [ann, willa]);
        var nodes = ById(after);
        nodes[willa].Y.Should().BeGreaterThan(nodes[ann].Y, "Willa is Ann's child");
        nodes[max].Y.Should().Be(nodes[willa].Y, "neither ex-partner has a date, so nothing separates their rows");
        Math.Abs(nodes[max].X - nodes[willa].X).Should().BeLessThanOrEqualTo(SpouseSpacingX,
            "Max must stay beside Willa, not move to a separate tree");
    }

    // R2 (2026-10-11): Pat (Gran's son) is married to Alex with a child, Kim. Pat then
    // gains two children with no other parent recorded, Kit and Kat, and Kit marries Zed.
    // That single-parent group was never placed: Kit and Kat landed past Pat's brother Sam
    // at the edge of the tree, and Zed was drawn on top of Kat.
    [Theory, InlineData(false), InlineData(true)]
    public void ChildrenWithNoOtherRecordedParent_SitUnderThatParentBesideTheirMarriage(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gran = t.Add("Gran", "Kuh", 1920);
        var sam = t.Add("Sam", "Kuh", 1948);
        var pat = t.Add("Pat", "Kuh", 1950);
        var alex = t.Add("Alex", "Lee", 1950);
        var kim = t.Add("Kim", "Kuh", 1975);
        t.Parent(gran, sam).Parent(gran, pat).Marry(pat, alex).Parent(pat, kim).Parent(alex, kim);

        var after = t.Clone();
        var kat = after.Add("Kat", "Kuh", 1970);
        var kit = after.Add("Kit", "Kuh", 1972);
        var zed = after.Add("Zed", "Zee", 1971);
        after.Parent(pat, kat).Parent(pat, kit).Marry(kit, zed);
        var layout = after.Layout(pat);

        AssertSound(after, layout);
        AssertChildUnderParents(after, layout, kim);
        // Kat and Kit sit outward of Kim, and Kit's marriage makes their slot wide, so
        // they aren't held to AssertChildUnderParents' one-SpouseSpacingX bound.
        var nodes = ById(layout);
        var samSide = Math.Sign(nodes[sam].X - nodes[pat].X);
        foreach (var child in new[] { kat, kit })
            Math.Sign(nodes[sam].X - nodes[child].X).Should().Be(samSide,
                $"{after.Name(child)} belongs under Pat, not past Pat's brother Sam");
    }

    // R2 (2026-10-11): Ann (Gran's daughter) has a son, Marc, with no other parent recorded,
    // so her primary group is a single-parent group. She then marries Lauren, who has no
    // other relatives. Only a couple could carry an attached marriage, so Lauren went to the
    // spouse safety net, which could walk off the left edge of the canvas.
    [Theory, InlineData(false), InlineData(true)]
    public void SingleParentMarriesSomeoneWithNoOtherRelatives_PartnerSitsBesideThem(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gran = t.Add("Gran", "Kuh", 1920);
        var sam = t.Add("Sam", "Kuh", 1946);
        var ann = t.Add("Ann", "Kuh", 1950);
        var max = t.Add("Max", "Kuh", 1944);
        var marc = t.Add("Marc", "Kuh", 1975);
        var lauren = t.Add("Lauren", "Lee", 1949);
        t.Parent(gran, sam).Parent(gran, ann).Parent(gran, max).Parent(ann, marc);

        var before = t.Layout(ann);
        var after = t.Clone().Marry(ann, lauren).Layout(ann);

        AssertSound(t, before);
        AssertSound(t, after);
        AssertChildUnderParents(t, after, marc);
        ById(after).Values.Should().OnlyContain(n => n.X >= ById(before).Values.Min(b => b.X),
            "nobody may be pushed past the left edge of the tree");
    }

    // R2 (a) from docs/TodoList.md (fuzz seed 880): Ellen Kuh, who has no parents on the
    // tree, was married to Max Small (Max Sr's son) and they have a child, Gish. The
    // marriage ends and she marries Ellen Small (Florence's daughter). Ellen Kuh belonged
    // to no family in the cross-root check, so the new marriage wasn't seen as joining two
    // families: Florence's side placed Ellen Kuh beside Ellen Small, on the far side from
    // Max, 520px from him.
    [Theory, InlineData(false), InlineData(true)]
    public void RemarriageIntoAnotherFamily_ParentlessPartnerSitsBetweenBothPartners(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var florence = t.Add("Florence", "Small", 1916);
        var maxSr = t.Add("Max", "Small", 1924);
        var ellenSmall = t.Add("Ellen", "Small", 1951);
        var ellenKuh = t.Add("Ellen", "Kuh", 1958);
        var maxJr = t.Add("Max", "Small", 1959);
        var gish = t.Add("Gish", "Kuh", 1988);
        t.Parent(florence, ellenSmall).Parent(maxSr, maxJr)
         .Marry(ellenKuh, maxJr).Parent(ellenKuh, gish).Parent(maxJr, gish);

        var before = t.Layout(ellenKuh);
        var after = t.Clone().EndMarriage(ellenKuh, maxJr, new DateOnly(1990, 1, 1))
                             .Marry(ellenKuh, ellenSmall).Layout(ellenKuh);

        AssertSound(t, before);
        AssertSound(t, after);
        AssertChildUnderParents(t, after, gish);
    }

    // R2 (2026-10-11): Rose (Ellen and Doug's daughter) married Bill from Dora's family to
    // the left, a cross-root couple. Ellen also has a son, Ben, with no other parent
    // recorded, and Ben is married to Gish. Ben's family hangs on Ellen's side of her
    // couple; on the side facing Dora's family it sat between Rose and Bill, 440px apart.
    // The couple is turned so it hangs on the far side.
    [Theory, InlineData(false), InlineData(true)]
    public void AttachedFamilyHangsAwayFromACrossRootMarriage(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var dora = t.Add("Dora", "Rosenberg", 1923);
        var ellen = t.Add("Ellen", "Rosenberg", 1926);
        var doug = t.Add("Doug", "Rosenberg", 1932);
        var bill = t.Add("Bill", "Rosenberg", 1967);
        var rose = t.Add("Rose", "Rosenberg", 1976);
        var max = t.Add("Max", "Rosenberg", 2002);
        t.Parent(dora, bill).Marry(ellen, doug).Parent(ellen, rose).Parent(doug, rose)
         .Marry(rose, bill).Parent(rose, max).Parent(bill, max);

        var after = t.Clone();
        var ben = after.Add("Ben", "Rosenberg", 1950);
        var gish = after.Add("Gish", "Lee", 1950);
        after.Parent(ellen, ben).Marry(ben, gish);

        AssertSound(t, t.Layout(bill));
        AssertSound(after, after.Layout(bill));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PARENTS AND CHILDREN
    // ─────────────────────────────────────────────────────────────────────────

    // Half-siblings: Pat (married to Alex, child Kim) also has a child, Kit, with Blair,
    // who is not Pat's spouse and comes from a separate family with her own parents.
    // Before: two separate trees. After: one tree, Pat in two groups (one married, one
    // co-parent only), Blair a descendant of a different root.
    //
    // Found 2026-10-11 (Leg 2): the cross-root step drops Kit at the midpoint correctly,
    // but Pat and Blair end up 460-660px apart. Pat is a root-group PARENT, and the
    // inner-edge sort only reorders children, so which side of his own couple Pat sits on
    // is decided by GUID order: with ascending ids, Alex sits between Pat and Blair.
    [Theory, InlineData(false), InlineData(true)]
    public void HalfSiblingWithAnUnmarriedCoParent_BothChildrenStayUnderTheirOwnParents(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var pat = t.Add("Pat", "Kuh", 1950);
        var alex = t.Add("Alex", "Kuh", 1950);
        var kim = t.Add("Kim", "Kuh", 1975);
        var bo = t.Add("Bo", "Lee", 1925);
        var bea = t.Add("Bea", "Lee", 1927);
        var blair = t.Add("Blair", "Lee", 1952);
        t.Marry(pat, alex).Parent(pat, kim).Parent(alex, kim)
         .Marry(bo, bea).Parent(bo, blair).Parent(bea, blair);

        var after = t.Clone();
        var kit = after.Add("Kit", "Kuh", 1980);
        after.Parent(pat, kit).Parent(blair, kit);
        var afterLayout = after.Layout(pat);

        AssertSound(after, afterLayout);
        AssertChildUnderParents(after, afterLayout, kim);
        AssertChildUnderParents(after, afterLayout, kit);
    }

    // A child recorded with one parent gains a second parent later (the other parent is
    // added, not married). The single-parent group becomes a couple group.
    [Theory, InlineData(false), InlineData(true)]
    public void SingleParentGainsACoParent_ChildSitsBetweenThem(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gus = t.Add("Gus", "Kuh", 1920);
        var pat = t.Add("Pat", "Kuh", 1950);
        var sis = t.Add("Sis", "Kuh", 1952);
        var kim = t.Add("Kim", "Kuh", 1975);
        t.Parent(gus, pat).Parent(gus, sis).Parent(pat, kim);

        var after = t.Clone();
        var quinn = after.Add("Quinn", "Lee", 1951);
        after.Parent(quinn, kim);
        var afterLayout = after.Layout(kim);

        AssertSound(after, afterLayout);
        AssertChildUnderParents(after, afterLayout, kim);
    }

    // Deleting a middle generation (soft delete removes the person and every relationship
    // touching them). Dad's wife and children lose their link to Dad's parents and become
    // a separate tree; the grandparents' side becomes a smaller tree of its own.
    [Theory, InlineData(false), InlineData(true)]
    public void DeletingTheMiddleGeneration_BothRemainingTreesStayIntact(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gpa = t.Add("Gus", "Small", 1900);
        var gma = t.Add("Gail", "Small", 1902);
        var dad = t.Add("Dan", "Small", 1930);
        var aunt = t.Add("Ada", "Small", 1933);
        var mom = t.Add("Mia", "Small", 1932);
        var k1 = t.Add("Kim", "Small", 1960);
        var k2 = t.Add("Kit", "Small", 1962);
        t.Marry(gpa, gma).Parent(gpa, dad).Parent(gma, dad).Parent(gpa, aunt).Parent(gma, aunt)
         .Marry(dad, mom).Parent(dad, k1).Parent(mom, k1).Parent(dad, k2).Parent(mom, k2);

        var before = t.Layout(k1);
        var after = t.Clone().Remove(dad);
        var afterLayout = after.Layout(k1);

        AssertSound(after, afterLayout);
        AssertChildUnderParents(after, afterLayout, k1);
        AssertChildUnderParents(after, afterLayout, k2);
        AssertChildUnderParents(after, afterLayout, aunt);
        // Not [mom, k1, k2] or [gpa, gma, aunt]: a parent legitimately re-centres over the
        // children they have left (Mia moves from one end of the Dan+Mia couple to the
        // centre of her children; Gus and Gail re-centre over Ada alone).
        AssertSameRelativePlacement(before, afterLayout, [k1, k2], "Mia's children keep their spacing");
        AssertSameRelativePlacement(before, afterLayout, [gpa, gma], "Gus and Gail stay a couple");
    }

    // Merging two trees with a Parent link: a separate family's root (Bea, with husband
    // Bob and son Ben) turns out to be the lost daughter of Ann and Andy. Before: two trees.
    // After: Bea's whole family nests one level under Ann and Andy.
    [Theory, InlineData(false), InlineData(true)]
    public void MergingTwoTreesWithAParentLink_FoundBranchKeepsItsShape(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var ann = t.Add("Ann", "Kuh", 1920);
        var andy = t.Add("Andy", "Kuh", 1918);
        var amy = t.Add("Amy", "Kuh", 1948);
        var bea = t.Add("Bea", "Lee", 1950);
        var bob = t.Add("Bob", "Lee", 1949);
        var ben = t.Add("Ben", "Lee", 1975);
        t.Marry(ann, andy).Parent(ann, amy).Parent(andy, amy)
         .Marry(bea, bob).Parent(bea, ben).Parent(bob, ben);

        var before = t.Layout(amy);
        var after = t.Clone().Parent(ann, bea).Parent(andy, bea);
        var afterLayout = after.Layout(amy);

        AssertSound(after, afterLayout);
        AssertChildUnderParents(after, afterLayout, amy);
        AssertChildUnderParents(after, afterLayout, bea);
        AssertChildUnderParents(after, afterLayout, ben);
        AssertSameRelativePlacement(before, afterLayout, [bea, bob, ben], "the found branch keeps its own shape");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SIBLINGS
    // ─────────────────────────────────────────────────────────────────────────

    // Fuzz seed 154 (2026-10-12), transcribed: a dateless family where Florence Small, an
    // orphan sister of Lauren Small, anchors off Lauren and walks one slot right, past the
    // end of the last family placed, and Marc Lee (Ann Lee's child by nobody on the tree,
    // placed by the safety net because Ann's primary group is her marriage) is then appended
    // at the end of the tree. The anchor passes never advanced that append position, so
    // Marc was drawn exactly on top of Florence. Found while fixing R6, which moved the
    // families enough to expose it.
    [Theory, InlineData(false), InlineData(true)]
    public void OrphanSiblingPastTheEndOfTheTree_IsNotDrawnUnderAnAppendedPerson(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var p = new[]
        {
            ("Ann", "Lee"), ("Sarah", "Fleishman"), ("Harvey", "Small"), ("Florence", "Rosenberg"),
            ("Lauren", "Rosenberg"), ("Ray", "Rosenberg"), ("Marc", "Lee"), ("Lauren", "Small"),
            ("Ann", "Rosenberg"), ("Florence", "Small"), ("Florence", "Small"), ("Harvey", "Small"),
            ("Willa", "Small"), ("Willa", "Rosenberg"), ("Harvey", "Lee"), ("Harvey", "Rosenberg"),
            ("Lauren", "Diaz"), ("Rose", "Lee"), ("Gish", "Lee"),
        }.Select(n => t.Add(n.Item1, n.Item2)).ToArray();
        t.Parent(p[3], p[4]).Parent(p[3], p[5]).Parent(p[0], p[6]).Parent(p[2], p[7]).Parent(p[3], p[8])
         .Parent(p[7], p[10]).Parent(p[2], p[11]).Parent(p[11], p[12]).Parent(p[5], p[13]).Parent(p[3], p[15])
         .Parent(p[0], p[17]).Parent(p[1], p[17])
         .Marry(p[8], p[18]).Marry(p[0], p[1], ended: new DateOnly(2000, 1, 1))
         .Sibling(p[14], p[0]).Sibling(p[2], p[1]).Sibling(p[9], p[7]);

        var layout = t.Layout(p[1]);

        var nodes = ById(layout);
        foreach (var a in p)
            foreach (var b in p.Where(b => b != a))
                (nodes[a].X, nodes[a].Y).Should().NotBe((nodes[b].X, nodes[b].Y),
                    $"{t.Name(a)} and {t.Name(b)} must not be drawn on the same spot");
    }

    // A Sibling link added between two people who both already have a spouse and children,
    // with no shared parent on the tree (Ray and Bill, self-service "we're brothers").
    // Neither is a root individual, so the sibling-anchor logic never runs; the link just
    // merges two trees into one component.
    [Theory, InlineData(false), InlineData(true)]
    public void SiblingLinkBetweenTwoMarriedParents_BothFamiliesKeepTheirShape(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var ray = t.Add("Ray", "Small", 1922);
        var rose = t.Add("Rose", "Small", 1924);
        var ellen = t.Add("Ellen", "Small", 1950);
        var bill = t.Add("Bill", "Small", 1925);
        var gish = t.Add("Gish", "Small", 1926);
        var willa = t.Add("Willa", "Small", 1955);
        t.Marry(ray, rose).Parent(ray, ellen).Parent(rose, ellen)
         .Marry(bill, gish).Parent(bill, willa).Parent(gish, willa);

        var before = t.Layout(ray);
        var after = t.Clone().Sibling(ray, bill).Layout(ray);

        AssertSound(t, after);
        AssertSameRelativePlacement(before, after, [ray, rose, ellen], "Ray's family keeps its shape");
        AssertSameRelativePlacement(before, after, [bill, gish, willa], "Bill's family keeps its shape");
    }

    // The same link, the other way round: Bill is a root individual (no spouse, no
    // children, no parents), so he IS placed by the sibling anchor. Then he marries Gish,
    // who has no other relatives. The orphan sibling gains a spouse.
    //
    // Found 2026-10-11 (Leg 2): not R8. Married, Bill+Gish become a root group of their
    // own and take their alphabetical slot ("Bill" before "Ray"), so they jump from beside
    // Ray to the far left and Ray's whole family shifts 320px right. The "before" state
    // also overlaps with ascending ids (Bill lands on Rose: R4).
    //
    // Fixed 2026-10-12 (R13, Doug's call: keep the couple beside the sibling's family): a
    // root group whose parents are Sibling-linked into another root group's family sorts
    // right after that group, so Bill+Gish land right of Ray's family, where Bill already sat.
    [Theory, InlineData(false), InlineData(true)]
    public void OrphanSiblingMarries_SpouseLandsBesideHimAndRaysFamilyDoesNotMove(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var ray = t.Add("Ray", "Small", 1922);
        var rose = t.Add("Rose", "Small", 1924);
        var ellen = t.Add("Ellen", "Small", 1950);
        var bill = t.Add("Bill", "Small", 1925);
        t.Marry(ray, rose).Parent(ray, ellen).Parent(rose, ellen).Sibling(ray, bill);

        var before = t.Layout(ray);
        var after = t.Clone();
        var gish = after.Add("Gish", "Small", 1926);
        after.Marry(bill, gish);
        var afterLayout = after.Layout(ray);

        AssertSound(after, afterLayout);
        AssertSamePositions(before, afterLayout, [ray, rose, ellen], "Bill's marriage must not move Ray's family");
    }

    // Same edit with a third root family in play: Ray's daughter Ellen married Marc, son of
    // Bud and Florence (a cross-root marriage), and "Bud" sorts before both "Bill" and "Ray".
    // Bill+Gish must still land next to Ray's family without wedging between the two
    // families the cross-root step keeps adjacent for Marc and Ellen.
    [Theory, InlineData(false), InlineData(true)]
    public void OrphanSiblingMarries_NextToAFamilyWithACrossRootMarriage(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var ray = t.Add("Ray", "Small", 1922);
        var rose = t.Add("Rose", "Small", 1924);
        var ellen = t.Add("Ellen", "Small", 1950);
        var bud = t.Add("Bud", "Kuh", 1920);
        var florence = t.Add("Florence", "Kuh", 1921);
        var marc = t.Add("Marc", "Kuh", 1948);
        var bill = t.Add("Bill", "Small", 1925);
        t.Marry(ray, rose).Parent(ray, ellen).Parent(rose, ellen)
         .Marry(bud, florence).Parent(bud, marc).Parent(florence, marc)
         .Marry(marc, ellen).Sibling(ray, bill);

        var before = t.Layout(ray);
        var after = t.Clone();
        var gish = after.Add("Gish", "Small", 1926);
        after.Marry(bill, gish);
        var afterLayout = after.Layout(ray);

        AssertSound(after, afterLayout);
        AssertSameRelativePlacement(before, afterLayout, [bud, florence, marc, ray, rose, ellen],
            "Bill's marriage must not rearrange the two families joined by Marc and Ellen");
        var nodes = ById(afterLayout);
        var rayFamily = new[] { ray, rose }.Select(id => nodes[id].X).ToList();
        Math.Min(Math.Abs(nodes[bill].X - rayFamily.Min()), Math.Abs(nodes[bill].X - rayFamily.Max()))
            .Should().BeLessThanOrEqualTo(2 * SpouseSpacingX, "Bill must sit next to his brother's family");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DATES
    // ─────────────────────────────────────────────────────────────────────────

    // A brand-new, fully dateless family (the state every newly invited family starts in)
    // gets its first birth date, on the youngest person. Inference now runs from that one
    // date instead of from the Pass 0.5 seed. Passes: this shape doesn't hit R3, which needs
    // competing evidence (several dated children, half-siblings) to go wrong.
    [Theory, InlineData(false), InlineData(true)]
    public void DatelessFamilyGainsItsFirstBirthDate_GenerationsStayInOrder(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var gus = t.Add("Gus", "Kuh");
        var gail = t.Add("Gail", "Kuh");
        var dan = t.Add("Dan", "Kuh");
        var ada = t.Add("Ada", "Kuh");
        var mia = t.Add("Mia", "Lee");
        var kim = t.Add("Kim", "Kuh");
        t.Marry(gus, gail).Parent(gus, dan).Parent(gail, dan).Parent(gus, ada).Parent(gail, ada)
         .Marry(dan, mia).Parent(dan, kim).Parent(mia, kim);

        var before = t.Layout(kim);
        var after = t.Clone().SetBirthYear(kim, 1990);
        var afterLayout = after.Layout(kim);

        AssertSound(t, before);
        AssertSound(after, afterLayout);
    }

    // R3 (fixed 2026-10-15): dateless birth-year inference took the first evidence it found,
    // so a dateless child could be inferred only a couple of years after a dateless parent.
    // x
    [Theory, InlineData(false), InlineData(true)]
    public void DatelessChildInferredFromHerOwnChild_StillRendersWellBelowHerDatelessParent(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var marc = t.Add("Marc", "Kuh");
        var willa = t.Add("Willa", "Kuh");
        var kim = t.Add("Kim", "Kuh", 1968);
        t.Parent(marc, willa).Parent(marc, kim);

        var before = t.Layout(kim);
        var zed = t.Clone();
        var zedId = zed.Add("Zed", "Kuh", 1970);
        zed.Parent(willa, zedId);
        var after = zed.Layout(kim);

        AssertSound(t, before);
        AssertSound(zed, after);
        var nodes = ById(after);
        (nodes[willa].Y - nodes[marc].Y).Should().BeGreaterThanOrEqualTo(20 * 6, "Willa is at least a generation below Marc");
    }

    // R3 (fixed 2026-10-15): two dateless partners were each inferred from whichever of their
    // children had a year, so a husband with two dated children and a wife with one landed on
    // different rows. Before: both partners share their one child. After: the husband gains a
    // second, older child from another relationship, which gives him different evidence. The
    // couple is dateless on both sides, so a Y difference is an artifact, not data.
    [Theory, InlineData(false), InlineData(true)]
    public void DatelessHusbandGainsAnOlderChildByAnother_CoupleStaysOnOneRow(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var marc = t.Add("Marc", "Small");
        var ann = t.Add("Ann", "Small");
        var kim = t.Add("Kim", "Small", 1960);
        t.Marry(marc, ann).Parent(marc, kim).Parent(ann, kim);

        var before = t.Layout(kim);
        var more = t.Clone();
        var lee = more.Add("Lee", "Small", 1950);
        more.Parent(marc, lee);
        var after = more.Layout(kim);

        AssertSound(t, before);
        AssertSound(more, after);
        ById(before)[marc].Y.Should().Be(ById(before)[ann].Y);
        ById(after)[marc].Y.Should().Be(ById(after)[ann].Y, "dateless partners render on the same row");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FOCUS
    // ─────────────────────────────────────────────────────────────────────────

    // Plan item: "a focus person who is removed or sits in a different component." Focus
    // only changes node SIZE (ComputeDepths starts from the first person in the list, not
    // the focus), so moving focus anywhere, including to an id that no longer exists,
    // must not move anybody.
    [Theory, InlineData(false), InlineData(true)]
    public void FocusMovesToAnotherTreeOrToADeletedPerson_NobodyMoves(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var ray = t.Add("Ray", "Small", 1922);
        var rose = t.Add("Rose", "Small", 1924);
        var ellen = t.Add("Ellen", "Small", 1950);
        var ann = t.Add("Ann", "Kuh", 1930);
        var amy = t.Add("Amy", "Kuh", 1958);
        t.Marry(ray, rose).Parent(ray, ellen).Parent(rose, ellen).Parent(ann, amy);

        var onRay = t.Layout(ray);
        var onAmy = t.Layout(amy);
        var onNobody = t.Layout(Guid.NewGuid());

        AssertSamePositions(onRay, onAmy, t.Ids, "focusing someone in another tree");
        AssertSamePositions(onRay, onNobody, t.Ids, "a focus id that is no longer on the tree");
        onNobody.Nodes.Should().NotContain(n => n.IsFocus);
    }

    // Not an edit: the same family read twice. GetAllAsync sorts by LastName, FirstName,
    // and SQL Server returns rows tied on both in no guaranteed order. Two children with the
    // same name (common historically: a child named after a sibling who died) used to swap
    // sides between page loads (R5, fixed 2026-10-17 by sorting inside the engine with Id
    // as the last key).
    [Theory, InlineData(false), InlineData(true)]
    public void SameNamedChildrenReturnedInEitherOrder_NobodyMoves(bool reverseIds)
    {
        var t = new Tree(reverseIds);
        var doug = t.Add("Doug", "Kuh", 1960);
        var ann = t.Add("Ann", "Kuh", 1962);
        var older = t.Add("Gish", "Kuh", 1990);
        var younger = t.Add("Gish", "Kuh", 1990);
        t.Marry(doug, ann).Parent(doug, older).Parent(ann, older).Parent(doug, younger).Parent(ann, younger);

        var asReturned = t.Layout(doug);
        var tiesReversed = t.Layout(doug, reverseNameTies: true);

        AssertSamePositions(asReturned, tiesReversed, t.Ids, "same-named rows coming back in the other order");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ASSERTIONS
    // ═════════════════════════════════════════════════════════════════════════

    private static Dictionary<Guid, LayoutNode> ById(FamilyTreeLayout layout) =>
        layout.Nodes.ToDictionary(n => n.Person.Id);

    // The fuzz harness's general invariants (1-4), plus the two its first run suggested
    // for Leg 2 (docs/TodoList.md, "Possible extra invariants"): nobody sits between two
    // spouses. A person in three couples can't be within SpouseSpacingX of all three
    // partners without an overlap, and with both sides taken one partner must have another
    // between them, so couple distance and "nobody between" are only checked for people
    // in at most maxCouplesPerPerson couples.
    private static void AssertSound(Tree t, FamilyTreeLayout layout, int maxCouplesPerPerson = 2)
    {
        var people = t.People();
        var nodes = ById(layout);
        var errors = new List<string>();

        foreach (var p in people)
        {
            var c = layout.Nodes.Count(n => n.Person.Id == p.Id);
            if (c != 1) errors.Add($"{t.Name(p.Id)} drawn {c} times");
        }
        if (errors.Count > 0) Fail(errors);

        var list = layout.Nodes;
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                var (a, b) = (list[i], list[j]);
                var dist = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
                if (dist < (a.Size + b.Size) / 2.0)
                    errors.Add($"{t.Name(a.Person.Id)} ({a.X},{a.Y}) overlaps {t.Name(b.Person.Id)} ({b.X},{b.Y})");
            }

        foreach (var child in people)
            foreach (var pid in child.ParentIds ?? [])
                if (nodes[child.Id].Y <= nodes[pid].Y)
                    errors.Add($"child {t.Name(child.Id)} y={nodes[child.Id].Y} is not below parent {t.Name(pid)} y={nodes[pid].Y}");

        var couples = CoupleHelper.Derive(people);
        int CoupleCount(Guid id) => couples.Count(c => c.PersonAId == id || c.PersonBId == id);
        foreach (var c in couples)
        {
            var (a, b) = (nodes[c.PersonAId], nodes[c.PersonBId]);
            if (CoupleCount(a.Person.Id) > maxCouplesPerPerson || CoupleCount(b.Person.Id) > maxCouplesPerPerson)
                continue;
            if (Math.Abs(a.X - b.X) > SpouseSpacingX)
                errors.Add($"couple {t.Name(a.Person.Id)} x={a.X} and {t.Name(b.Person.Id)} x={b.X} are {Math.Abs(a.X - b.X)}px apart");

            var (left, right) = a.X < b.X ? (a, b) : (b, a);
            var top = Math.Min(a.Y, b.Y) - a.Size / 2;
            var bottom = Math.Max(a.Y, b.Y) + a.Size / 2;
            foreach (var n in layout.Nodes.Where(n => n.X > left.X && n.X < right.X && n.Y >= top && n.Y <= bottom))
                errors.Add($"{t.Name(n.Person.Id)} x={n.X} sits between partners {t.Name(left.Person.Id)} x={left.X} and {t.Name(right.Person.Id)} x={right.X}");
        }
        Fail(errors);
    }

    // Second suggested invariant: a child sits under its own parents. With one parent,
    // within one SpouseSpacingX of them; with two, between them (with that much slack
    // either side). Not a general invariant (a wide family spreads children further), so
    // only asserted on the small families here.
    private static void AssertChildUnderParents(Tree t, FamilyTreeLayout layout, Guid childId)
    {
        var nodes = ById(layout);
        var parentXs = t.People().Single(p => p.Id == childId).ParentIds!.Select(pid => nodes[pid].X).ToList();
        var x = nodes[childId].X;
        (x >= parentXs.Min() - SpouseSpacingX && x <= parentXs.Max() + SpouseSpacingX).Should().BeTrue(
            $"{t.Name(childId)} (x={x}) must sit under their parents (x={string.Join(", ", parentXs)})");
    }

    private static void AssertSamePositions(FamilyTreeLayout before, FamilyTreeLayout after, IEnumerable<Guid> ids, string because = "")
    {
        var (b, a) = (ById(before), ById(after));
        foreach (var id in ids)
            (a[id].X, a[id].Y).Should().Be((b[id].X, b[id].Y), $"{a[id].Person.FirstName} must not move: {because}");
    }

    // The listed people may move, but only together, as a rigid block (a neighbour grew,
    // or the timeline origin moved).
    private static void AssertSameRelativePlacement(FamilyTreeLayout before, FamilyTreeLayout after, IReadOnlyList<Guid> ids, string because)
    {
        var (b, a) = (ById(before), ById(after));
        var (dx, dy) = (a[ids[0]].X - b[ids[0]].X, a[ids[0]].Y - b[ids[0]].Y);
        foreach (var id in ids)
            (a[id].X - b[id].X, a[id].Y - b[id].Y).Should().Be((dx, dy),
                $"{a[id].Person.FirstName} moved differently from {a[ids[0]].Person.FirstName}: {because}");
    }

    private static void Fail(List<string> errors)
    {
        if (errors.Count > 0)
            throw new Xunit.Sdk.XunitException($"{errors.Count} violation(s):\n  " + string.Join("\n  ", errors));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FAMILY BUILDER
    // ═════════════════════════════════════════════════════════════════════════

    // Person + Relationship rows, mapped exactly like PersonService.GetAllAsync
    // (PersonMapper, then LastName/FirstName order). Ids are deterministic so a failure
    // replays; reverseIds flips their order.
    private sealed class Tree(bool reverseIds)
    {
        private readonly List<Person> _people = [];
        private readonly List<Relationship> _rels = [];

        public IReadOnlyList<Guid> Ids => _people.Select(p => p.Id).ToList();

        public Guid Add(string first, string last, int? year = null)
        {
            var n = _people.Count + 1;
            var id = new Guid(reverseIds ? 1000 - n : n, 0, 0, new byte[8]);
            _people.Add(new Person
            {
                Id = id, FirstName = first, LastName = last,
                BirthDate = year is { } y ? new DateOnly(y, 6, 1) : null,
            });
            return id;
        }

        public Tree Parent(Guid parent, Guid child) => Rel(RelationshipType.Parent, parent, child);
        public Tree Sibling(Guid a, Guid b) => Rel(RelationshipType.Sibling, a, b);
        public Tree Marry(Guid a, Guid b, DateOnly? ended = null) => Rel(RelationshipType.Spouse, a, b, ended);

        public Tree EndMarriage(Guid a, Guid b, DateOnly on)
        {
            _rels.Single(r => r.Type == RelationshipType.Spouse &&
                              ((r.PersonAId == a && r.PersonBId == b) || (r.PersonAId == b && r.PersonBId == a)))
                 .EndDate = on;
            return this;
        }

        public Tree SetBirthYear(Guid id, int year)
        {
            _people.Single(p => p.Id == id).BirthDate = new DateOnly(year, 6, 1);
            return this;
        }

        // Soft delete: PersonService.DeleteAsync stamps the person and every relationship
        // touching them, and both are filtered out of GetAllAsync.
        public Tree Remove(Guid id)
        {
            _people.RemoveAll(p => p.Id == id);
            _rels.RemoveAll(r => r.PersonAId == id || r.PersonBId == id);
            return this;
        }

        private Tree Rel(RelationshipType type, Guid a, Guid b, DateOnly? end = null)
        {
            // Canonical ordering (PersonAId < PersonBId) for the bidirectional types.
            if (type != RelationshipType.Parent && b.CompareTo(a) < 0) (a, b) = (b, a);
            _rels.Add(new Relationship { Id = Guid.NewGuid(), Type = type, PersonAId = a, PersonBId = b, EndDate = end });
            return this;
        }

        public Tree Clone()
        {
            var copy = new Tree(reverseIds);
            copy._people.AddRange(_people.Select(p => new Person
            {
                Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, BirthDate = p.BirthDate,
            }));
            copy._rels.AddRange(_rels.Select(r => new Relationship
            {
                Id = r.Id, Type = r.Type, PersonAId = r.PersonAId, PersonBId = r.PersonBId, EndDate = r.EndDate,
            }));
            return copy;
        }

        public List<PersonDto> People() => _people
            .Select(p => PersonMapper.MapPersonToDto(p, _rels))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .ToList();

        // reverseNameTies reverses the order of rows tied on LastName and FirstName, the
        // part of the list order SQL Server doesn't guarantee.
        public FamilyTreeLayout Layout(Guid focus, bool reverseNameTies = false)
        {
            var people = People();
            if (reverseNameTies)
                people = [.. people.GroupBy(p => (p.LastName, p.FirstName)).SelectMany(g => g.Reverse())];
            return new FamilyTreeLayoutEngine().ComputeLayout(people, CoupleHelper.Derive(people), focus);
        }

        public string Name(Guid id) => _people.FirstOrDefault(p => p.Id == id)?.FirstName ?? id.ToString()[..8];
    }
}
