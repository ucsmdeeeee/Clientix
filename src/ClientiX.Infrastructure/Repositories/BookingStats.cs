namespace ClientiX.Infrastructure.Repositories;

public class BookingStats
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int NoShow { get; set; }
    public int CancelledByClient { get; set; }
    public int CancelledByMaster { get; set; }
    public int Upcoming { get; set; }
    public int RevenueRub { get; set; }
}