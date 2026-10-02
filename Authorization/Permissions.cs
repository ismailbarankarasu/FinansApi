using FinansApi.Data.Accounting;

namespace FinansApi.Authorization;

public static class Permissions
{
    public const string Read = "Read";
    public const string SalesRead = "Sales.Read";
    public const string ManageCompany = "Company.Manage";
    public const string ManageMembers = "Members.Manage";
    public const string PostJournal = "Journal.Post";
    public const string DraftSalesInvoice = "Invoice.DraftSales";
    public const string WriteCustomer = "Customer.Write";
    public const string ApproveInvoice = "Invoice.Approve";
    public const string CreatePayment = "Payment.Create";
    public const string ReadReports = "Reports.Read";
    public const string ClosePeriod = "Period.Close";
    public const string ReadAudit = "Audit.Read";
    public static bool IsGranted(CompanyRole role, string permission)
    {
        var read = permission is Read or SalesRead or ReadReports;
        var accounting = permission is PostJournal or DraftSalesInvoice or WriteCustomer or ApproveInvoice or CreatePayment or ClosePeriod or ReadAudit;
        var administration = permission is ManageCompany or ManageMembers;
        return role switch
        {
            CompanyRole.Admin => read || accounting || administration,
            CompanyRole.Accountant => read || accounting,
            CompanyRole.Reader => read,
            CompanyRole.Sales => permission is SalesRead or DraftSalesInvoice or WriteCustomer,
            _ => false
        };
    }

}
