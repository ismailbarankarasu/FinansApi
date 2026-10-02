using FinansApi.Data.Accounting;

namespace FinansApi.Services.Accounting;

public static class ReportAccountMapping
{
    public static bool IsBalanceSheetAccount(AccountClass accountClass) => accountClass is AccountClass.Asset or AccountClass.Liability or AccountClass.Equity;

    public static bool IsOperatingAccount(AccountClass accountClass) => accountClass is AccountClass.Income or AccountClass.Expense;

    public static decimal NetBalance(IEnumerable<LedgerRow> rows, AccountClass accountClass) => rows.Where(row => row.AccountClass == accountClass).Sum(row => row.Debit - row.Credit);

}
