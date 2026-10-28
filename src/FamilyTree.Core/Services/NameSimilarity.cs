namespace FamilyTree.Core.Services;

/// <summary>
/// Name comparison for the duplicate checks: edit distance, and first names that are the
/// same name in a different form ("Bill" and "William"). Edit distance alone can't see the
/// latter: "Bill Small" vs "William Small" is only about 0.6 similar.
/// </summary>
public static class NameSimilarity
{
    // Each group is one name and its common short forms. A name can sit in several groups
    // ("Al" is Albert, Alfred or Alan).
    private static readonly string[][] NicknameGroups =
    [
        ["william", "bill", "billy", "will", "willie", "willy"],
        ["robert", "bob", "bobby", "rob", "robbie", "bert"],
        ["richard", "dick", "rick", "ricky", "rich"],
        ["james", "jim", "jimmy", "jamie"],
        ["john", "jack", "johnny", "jon"],
        ["jonathan", "jon", "jonny"],
        ["joseph", "joe", "joey"],
        ["margaret", "peggy", "maggie", "meg", "marge", "margie"],
        ["elizabeth", "liz", "lizzie", "beth", "betty", "betsy", "libby", "eliza"],
        ["katherine", "catherine", "kathryn", "kate", "katie", "kathy", "cathy", "kay"],
        ["edward", "ed", "eddie", "ted", "ned"],
        ["theodore", "ted", "teddy", "theo"],
        ["michael", "mike", "mikey", "mickey"],
        ["thomas", "tom", "tommy"],
        ["charles", "charlie", "chuck", "chas"],
        ["daniel", "dan", "danny"],
        ["david", "dave", "davey"],
        ["samuel", "sam", "sammy"],
        ["benjamin", "ben", "benny"],
        ["abraham", "abe", "abie"],
        ["isaac", "ike"],
        ["jacob", "jake", "jack"],
        ["morris", "moe", "morrie"],
        ["moses", "moe", "moshe"],
        ["louis", "lou", "louie"],
        ["harold", "harry", "hal"],
        ["henry", "hank", "harry", "hal"],
        ["frederick", "fred", "freddie", "freddy"],
        ["lawrence", "laurence", "larry"],
        ["leonard", "len", "lenny", "leo"],
        ["raymond", "ray"],
        ["ronald", "ron", "ronnie"],
        ["donald", "don", "donnie"],
        ["gerald", "jerry", "gerry"],
        ["stephen", "steven", "steve", "stevie"],
        ["patricia", "pat", "patty", "trish"],
        ["patrick", "pat", "paddy"],
        ["susan", "sue", "susie", "suzy"],
        ["deborah", "debra", "debbie", "deb"],
        ["rebecca", "becky", "becca"],
        ["florence", "flo", "flossie"],
        ["dorothy", "dot", "dottie", "dora"],
        ["barbara", "barb", "barbie", "babs"],
        ["judith", "judy", "jodi"],
        ["mildred", "millie"],
        ["eleanor", "elinor", "ellie", "nell", "nellie", "nora"],
        ["ellen", "ellie", "nell"],
        ["helen", "nellie", "nell", "lena"],
        ["frances", "fran", "frannie", "fannie", "fanny"],
        ["francis", "frank", "frankie"],
        ["albert", "al", "bert", "bertie"],
        ["alfred", "al", "alf", "fred"],
        ["alan", "allan", "allen", "al"],
        ["nathan", "nate", "nat"],
        ["nathaniel", "nate", "nat", "nathan"],
        ["matthew", "matt", "matty"],
        ["andrew", "andy", "drew"],
        ["anthony", "tony"],
        ["peter", "pete"],
        ["gregory", "greg"],
        ["jeffrey", "geoffrey", "jeff"],
        ["kenneth", "ken", "kenny"],
        ["walter", "walt", "wally"],
        ["eugene", "gene"],
        ["herbert", "herb", "herbie"],
        ["bernard", "bernie"],
        ["sidney", "sydney", "sid"],
        ["seymour", "sy", "cy"],
        ["irving", "irv"],
        ["marvin", "marv"],
        ["martin", "marty"],
        ["mitchell", "mitch"],
        ["joshua", "josh"],
        ["zachary", "zach", "zack"],
        ["alexander", "alex", "sandy", "xander"],
        ["alexandra", "alex", "sandy", "lexi"],
        ["victoria", "vicky", "vicki", "tori"],
        ["jennifer", "jen", "jenny"],
        ["jessica", "jess", "jessie"],
        ["christine", "christina", "chris", "chrissy", "tina"],
        ["christopher", "chris", "kit"],
        ["nicholas", "nick", "nicky"],
        ["timothy", "tim", "timmy"],
        ["philip", "phillip", "phil"],
        ["douglas", "doug"],
        ["gertrude", "gertie", "trudy"],
        ["esther", "essie", "etta"],
        ["sarah", "sara", "sally", "sadie"],
        ["mary", "molly", "polly", "mae", "mamie"],
        ["anne", "ann", "anna", "annie", "nancy", "hannah"],
        ["lillian", "lilian", "lil", "lily", "lilly"],
        ["rose", "rosie", "rosa"],
        ["miriam", "mimi", "mollie"],
        ["sylvia", "syl"],
        ["evelyn", "evie", "eve"],
        ["beatrice", "bea", "trixie"],
        ["harriet", "hattie"],
        ["josephine", "jo", "josie"],
        ["virginia", "ginny", "ginger"],
    ];

    private static readonly Dictionary<string, HashSet<int>> GroupsOf = BuildGroups();

    private static Dictionary<string, HashSet<int>> BuildGroups()
    {
        var map = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        for (var g = 0; g < NicknameGroups.Length; g++)
            foreach (var name in NicknameGroups[g])
            {
                if (!map.TryGetValue(name, out var set)) map[name] = set = [];
                set.Add(g);
            }
        return map;
    }

    /// <summary>The same first name, ignoring case and spaces, or two forms of one name
    /// ("Bill" and "William").</summary>
    public static bool SameFirstName(string? a, string? b)
    {
        var x = (a ?? "").Trim();
        var y = (b ?? "").Trim();
        if (x.Length == 0 || y.Length == 0) return false;
        if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase)) return true;
        return GroupsOf.TryGetValue(x, out var gx) && GroupsOf.TryGetValue(y, out var gy) && gx.Overlaps(gy);
    }

    /// <summary>0 (nothing alike) to 1 (identical, ignoring case), by edit distance.</summary>
    public static double Similarity(string s1, string s2)
    {
        if (s1.Length == 0 && s2.Length == 0) return 1.0;
        if (s1.Length == 0 || s2.Length == 0) return 0.0;
        return 1.0 - (double)Distance(s1, s2) / Math.Max(s1.Length, s2.Length);
    }

    /// <summary>Levenshtein distance, ignoring case: the fewest single-letter insertions,
    /// deletions and substitutions that turn one string into the other.</summary>
    public static int Distance(string s1, string s2)
    {
        s1 = s1.ToLowerInvariant();
        s2 = s2.ToLowerInvariant();
        var m = s1.Length;
        var n = s2.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) dp[i, 0] = i;
        for (var j = 0; j <= n; j++) dp[0, j] = j;

        for (var i = 1; i <= m; i++)
            for (var j = 1; j <= n; j++)
                dp[i, j] = s1[i - 1] == s2[j - 1]
                    ? dp[i - 1, j - 1]
                    : 1 + Math.Min(Math.Min(dp[i - 1, j], dp[i, j - 1]), dp[i - 1, j - 1]);

        return dp[m, n];
    }
}
