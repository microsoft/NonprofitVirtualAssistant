namespace NVA.Models
{
    // Moderation result has more fields for detailed categorization of response which are not included here
    public class ModerationResult
    {
        public bool Flagged { get; set; }
    }

    public class ModerationResponse
    {
        public string Id { get; set; }
        public string Model { get; set; }
        public ModerationResult[] Results { get; set; }
    }
}
