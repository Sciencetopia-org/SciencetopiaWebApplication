    public class UpdateStatusRequest
    {
        public string? UserId { get; set; }
        public string? StudyGroupId { get; set; }
        public string? Status { get; set; } // Ensure this is validated (e.g., "Approved", "Rejected")
    }