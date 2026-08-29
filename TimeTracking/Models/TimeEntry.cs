namespace TimeTracking.Models;

public class TimeEntry
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public Task Task { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
