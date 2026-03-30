using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.ViewModels;

public class InvoiceGroup(string monthLabel, int year, int month) : List<InvoiceDto>
{
    public string MonthLabel { get; } = monthLabel;
    public int Year { get; } = year;
    public int Month { get; } = month;
}
