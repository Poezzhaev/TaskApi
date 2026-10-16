namespace TaskApi
{
    public class TaskItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public bool IsCompleted { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; } // когда завершили
        public Priority Priority { get; set; } // Low, Medium, High

    }

    public enum Priority
    {
        Low, Medium, Higt
    }
}
