namespace Sciencetopia.DTOs
{
    public class SaveStudyPlanDraftRequest
    {
        public StudyPlanDTO? Payload { get; set; }
        public string? ChangeNotes { get; set; }
    }

    public class PublishStudyPlanDraftRequest
    {
        public string? StudyPlanId { get; set; }
        public int DraftNumber { get; set; }
        public string? ChangeNotes { get; set; }
    }
}

