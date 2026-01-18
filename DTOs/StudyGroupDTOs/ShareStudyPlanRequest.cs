namespace Sciencetopia.DTOs
{
    public class ShareStudyPlanRequest
    {
        public string Permission { get; set; } = "view"; // view|comment|edit|admin
        public bool AutoEnroll { get; set; } = false;
        public int? VersionNumber { get; set; }
    }
}
