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

public class DailyBookingCount
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}