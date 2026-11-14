namespace Lilia.Api.Services;

/// <summary>
/// Unused Ask Lilia credits roll over (the owner, 14 Nov 2026, "Capped, 3 months"): what a month leaves unused carries
/// into the next, never more than one month's allowance, credits expire after 3 months, and they are lost when the plan
/// ends (the caller counts only months since the current plan started).
///
/// The oldest credits are spent first and the cap keeps the newest, so a month's credits are either used or gone by
/// the end of the month after it: none outlives its third month, and the expiry needs no step of its own.
/// </summary>
internal static class AskRollover
{
    /// <summary>How many past months to replay: the credits' life. Carried credits are always last month's.</summary>
    public const int LookBackMonths = 3;

    /// <summary>
    /// The credits carried into this month, given the allowance and what each past month spent, oldest first (the
    /// last is the month just ended). A month that spent more than it had carries nothing.
    /// </summary>
    public static int Carry(int allowance, IReadOnlyList<int> spentByMonth)
    {
        if (allowance <= 0) return 0;
        var carry = 0;
        foreach (var spent in spentByMonth)
            carry = Math.Clamp(allowance + carry - Math.Max(0, spent), 0, allowance);
        return carry;
    }
}
